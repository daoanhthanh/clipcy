use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU32, AtomicU64, Ordering};
use std::time::{Duration, Instant};

use parking_lot::{Mutex, RwLock};
use tokio::runtime::Handle;
use tokio::sync::{mpsc, oneshot};
use uuid::Uuid;

use crate::domain::{ClipboardItem, Settings};
use crate::error::{ClipboardError, Result};
use crate::ports::{ClipboardMonitor, ClipboardObservation, ClipboardRepository, PasteHandler};

const DEBOUNCE_DURATION_MS: u64 = 300;
const PASTE_SUPPRESS_DURATION_MS: u64 = 1000;

struct PublishedCapture {
    paused: AtomicBool,
    epoch: AtomicU64,
    baseline: AtomicU32,
    acknowledged_paused: AtomicBool,
}

impl PublishedCapture {
    fn new() -> Self {
        Self {
            paused: AtomicBool::new(false),
            epoch: AtomicU64::new(1),
            baseline: AtomicU32::new(0),
            acknowledged_paused: AtomicBool::new(false),
        }
    }
}

enum DebounceMessage {
    Item(ClipboardObservation),
    Flush(oneshot::Sender<Result<()>>),
    SetPaused {
        paused: bool,
        epoch: u64,
        response: oneshot::Sender<Result<()>>,
    },
}

async fn save_pending_item(
    pending_item: &mut Option<ClipboardItem>,
    repository: &Arc<dyn ClipboardRepository>,
    settings: &Arc<RwLock<Settings>>,
    on_change_callback: &Arc<RwLock<Option<Box<dyn Fn() + Send + Sync>>>>,
) -> Result<()> {
    if let Some(item) = pending_item.take() {
        let max_items = settings.read().max_history_items;
        tracing::debug!(
            id = %item.id,
            content_type = ?item.content_type,
            data_len = item.data.len(),
            "Saving debounced clipboard item"
        );
        repository.save(&item).await?;
        let pruned = repository.prune(max_items).await?;
        tracing::debug!(pruned, "Pruned clipboard history after save");

        if let Some(callback) = on_change_callback.read().as_ref() {
            callback();
        }
    }
    Ok(())
}

fn admit_observation(observation: &ClipboardObservation, published: &PublishedCapture) -> bool {
    if observation.epoch != published.epoch.load(Ordering::SeqCst) {
        return false;
    }
    if published.paused.load(Ordering::SeqCst) {
        return false;
    }
    let baseline = published.baseline.load(Ordering::SeqCst);
    if baseline != 0 && (observation.sequence == 0 || observation.sequence == baseline) {
        return false;
    }
    true
}

pub struct ClipboardService {
    monitor: Arc<dyn ClipboardMonitor>,
    repository: Arc<dyn ClipboardRepository>,
    paste_handler: Arc<dyn PasteHandler>,
    settings: Arc<RwLock<Settings>>,
    debounce_tx: Arc<RwLock<Option<mpsc::UnboundedSender<DebounceMessage>>>>,
    suppress_until: Arc<RwLock<Option<Instant>>>,
    runtime_handle: Handle,
    on_change_callback: Arc<RwLock<Option<Box<dyn Fn() + Send + Sync>>>>,
    published: Arc<PublishedCapture>,
    policy_lock: Mutex<()>,
}

impl ClipboardService {
    pub fn new(
        monitor: Arc<dyn ClipboardMonitor>,
        repository: Arc<dyn ClipboardRepository>,
        paste_handler: Arc<dyn PasteHandler>,
        settings: Settings,
        runtime_handle: Handle,
    ) -> Self {
        Self {
            monitor,
            repository,
            paste_handler,
            settings: Arc::new(RwLock::new(settings)),
            debounce_tx: Arc::new(RwLock::new(None)),
            suppress_until: Arc::new(RwLock::new(None)),
            runtime_handle,
            on_change_callback: Arc::new(RwLock::new(None)),
            published: Arc::new(PublishedCapture::new()),
            policy_lock: Mutex::new(()),
        }
    }

    pub fn start_monitoring(&self) -> Result<()> {
        let _guard = self.policy_lock.lock();
        tracing::info!("Starting clipboard monitoring");
        self.published.paused.store(false, Ordering::SeqCst);
        self.published.epoch.store(1, Ordering::SeqCst);
        self.published.baseline.store(0, Ordering::SeqCst);
        self.published
            .acknowledged_paused
            .store(false, Ordering::SeqCst);
        self.monitor.apply_capture_policy(false, 1, None)?;

        let (tx, rx) = mpsc::unbounded_channel::<DebounceMessage>();
        *self.debounce_tx.write() = Some(tx.clone());

        let repository = Arc::clone(&self.repository);
        let settings = Arc::clone(&self.settings);
        let suppress_until = Arc::clone(&self.suppress_until);
        let on_change_callback = Arc::clone(&self.on_change_callback);
        let published = Arc::clone(&self.published);

        self.spawn_debounce_task(
            rx,
            repository,
            settings,
            suppress_until,
            on_change_callback,
            published,
        );

        let debounce_tx = Arc::clone(&self.debounce_tx);
        self.monitor.on_change(Box::new(move |observation| {
            if let Some(tx) = debounce_tx.read().as_ref() {
                let _ = tx.send(DebounceMessage::Item(observation));
            }
        }));

        self.monitor.start()
    }

    fn spawn_debounce_task(
        &self,
        mut rx: mpsc::UnboundedReceiver<DebounceMessage>,
        repository: Arc<dyn ClipboardRepository>,
        settings: Arc<RwLock<Settings>>,
        suppress_until: Arc<RwLock<Option<Instant>>>,
        on_change_callback: Arc<RwLock<Option<Box<dyn Fn() + Send + Sync>>>>,
        published: Arc<PublishedCapture>,
    ) {
        self.runtime_handle.spawn(async move {
            let debounce_duration = Duration::from_millis(DEBOUNCE_DURATION_MS);
            let mut pending_item: Option<ClipboardItem> = None;
            let mut pending_epoch: u64 = 0;

            loop {
                let timeout = if pending_item.is_some() {
                    tokio::time::timeout(debounce_duration, rx.recv())
                } else {
                    tokio::time::timeout(Duration::from_secs(86400), rx.recv())
                };

                match timeout.await {
                    Ok(Some(message)) => match message {
                        DebounceMessage::Item(observation) => {
                            if !admit_observation(&observation, &published) {
                                tracing::debug!(
                                    epoch = observation.epoch,
                                    sequence = observation.sequence,
                                    "Clipboard observation rejected by capture gate"
                                );
                                continue;
                            }

                            let suppress_deadline = *suppress_until.read();
                            if let Some(deadline) = suppress_deadline {
                                if Instant::now() <= deadline {
                                    tracing::debug!(
                                        "Clipboard update suppressed due to recent paste"
                                    );
                                    continue;
                                }
                                *suppress_until.write() = None;
                            }

                            let item = observation.item;
                            tracing::debug!(
                                id = %item.id,
                                content_type = ?item.content_type,
                                data_len = item.data.len(),
                                preview_len = item.preview.len(),
                                thumbnail_len = item.thumbnail.as_ref().map(|t| t.len()).unwrap_or(0),
                                source_app = item.source_app.as_deref(),
                                "Clipboard item queued"
                            );
                            pending_epoch = observation.epoch;
                            pending_item = Some(item);
                        }
                        DebounceMessage::Flush(response) => {
                            tracing::debug!("Flush requested for pending clipboard item");
                            if published.paused.load(Ordering::SeqCst) {
                                pending_item = None;
                                let _ = response.send(Ok(()));
                                continue;
                            }
                            let result = save_pending_item(
                                &mut pending_item,
                                &repository,
                                &settings,
                                &on_change_callback,
                            )
                            .await;
                            let _ = response.send(result);
                        }
                        DebounceMessage::SetPaused {
                            paused,
                            epoch,
                            response,
                        } => {
                            if paused || pending_epoch != epoch {
                                pending_item = None;
                            }
                            published
                                .acknowledged_paused
                                .store(paused, Ordering::SeqCst);
                            tracing::debug!(paused, epoch, "Capture pause barrier acknowledged");
                            let _ = response.send(Ok(()));
                        }
                    },
                    Ok(None) => {
                        break;
                    }
                    Err(_) => {
                        if published.paused.load(Ordering::SeqCst) {
                            pending_item = None;
                            continue;
                        }
                        if let Err(e) = save_pending_item(
                            &mut pending_item,
                            &repository,
                            &settings,
                            &on_change_callback,
                        )
                        .await
                        {
                            tracing::error!("Failed to save clipboard item: {}", e);
                        }
                    }
                }
            }
        });
    }

    pub fn stop_monitoring(&self) -> Result<()> {
        let _guard = self.policy_lock.lock();
        *self.debounce_tx.write() = None;
        self.published.paused.store(false, Ordering::SeqCst);
        self.published.epoch.store(1, Ordering::SeqCst);
        self.published.baseline.store(0, Ordering::SeqCst);
        self.published
            .acknowledged_paused
            .store(false, Ordering::SeqCst);
        let _ = self.monitor.apply_capture_policy(false, 1, None);
        self.monitor.stop()
    }

    pub fn is_capture_paused(&self) -> bool {
        self.published.acknowledged_paused.load(Ordering::SeqCst)
    }

    /// Pause or resume new capture. Restart leaves capture active.
    ///
    /// Pause disables native decode immediately, drops queued and pending
    /// ordinary content, and acknowledges only after the worker has settled
    /// that barrier. Resume samples the current sequence as a noncapturable
    /// baseline and does not import the clipboard copied while paused. An
    /// unavailable sequence leaves the previous policy unchanged.
    pub async fn set_capture_paused(&self, paused: bool) -> Result<()> {
        let _guard = self.policy_lock.lock();
        let sender = self.debounce_tx.read().clone().ok_or_else(|| {
            ClipboardError::PlatformNotSupported("monitoring is not running".into())
        })?;

        let previous_paused = self.published.paused.load(Ordering::SeqCst);
        let previous_epoch = self.published.epoch.load(Ordering::SeqCst);
        let previous_baseline_raw = self.published.baseline.load(Ordering::SeqCst);
        let previous_baseline = (previous_baseline_raw != 0).then_some(previous_baseline_raw);

        let epoch = previous_epoch.saturating_add(1);
        let resume_baseline = if paused {
            None
        } else {
            match self.monitor.sample_sequence() {
                Ok(sequence) if sequence != 0 => Some(sequence),
                _ => {
                    return Err(ClipboardError::ClipboardAccess(
                        "Clipboard sequence unavailable; capture remains paused".into(),
                    ));
                }
            }
        };

        if let Err(error) = self
            .monitor
            .apply_capture_policy(paused, epoch, resume_baseline)
        {
            let _ = self.monitor.apply_capture_policy(
                previous_paused,
                previous_epoch,
                previous_baseline,
            );
            return Err(error);
        }

        self.published
            .baseline
            .store(resume_baseline.unwrap_or(0), Ordering::SeqCst);
        self.published.epoch.store(epoch, Ordering::SeqCst);
        self.published.paused.store(paused, Ordering::SeqCst);

        let (response_tx, response_rx) = oneshot::channel();
        if sender
            .send(DebounceMessage::SetPaused {
                paused,
                epoch,
                response: response_tx,
            })
            .is_err()
        {
            let _ = self.monitor.apply_capture_policy(
                previous_paused,
                previous_epoch,
                previous_baseline,
            );
            self.published.paused.store(previous_paused, Ordering::SeqCst);
            self.published.epoch.store(previous_epoch, Ordering::SeqCst);
            self.published
                .baseline
                .store(previous_baseline.unwrap_or(0), Ordering::SeqCst);
            return Err(ClipboardError::PlatformNotSupported(
                "capture worker is not running".into(),
            ));
        }

        match response_rx.await {
            Ok(Ok(())) => Ok(()),
            Ok(Err(error)) => {
                let _ = self.monitor.apply_capture_policy(
                    previous_paused,
                    previous_epoch,
                    previous_baseline,
                );
                self.published.paused.store(previous_paused, Ordering::SeqCst);
                self.published.epoch.store(previous_epoch, Ordering::SeqCst);
                self.published
                    .baseline
                    .store(previous_baseline.unwrap_or(0), Ordering::SeqCst);
                Err(error)
            }
            Err(_) => Err(ClipboardError::PlatformNotSupported(
                "capture worker dropped the pause acknowledgement".into(),
            )),
        }
    }

    pub async fn flush_pending(&self) -> Result<()> {
        let sender = self.debounce_tx.read().clone();
        let Some(sender) = sender else {
            return Ok(());
        };

        let (response_tx, response_rx) = oneshot::channel();
        if sender.send(DebounceMessage::Flush(response_tx)).is_err() {
            return Ok(());
        }

        match response_rx.await {
            Ok(result) => result,
            Err(_) => Ok(()),
        }
    }

    pub async fn get_history(&self, limit: Option<usize>) -> Result<Vec<ClipboardItem>> {
        let limit = limit.unwrap_or(self.settings.read().max_history_items);
        self.repository.get_all(limit).await
    }

    pub async fn paste_item(&self, id: Uuid) -> Result<()> {
        let item = self.repository.get_by_id(id).await?;

        if let Some(item) = item {
            *self.suppress_until.write() =
                Some(Instant::now() + Duration::from_millis(PASTE_SUPPRESS_DURATION_MS));
            if let Err(err) = self.monitor.set_content(&item) {
                *self.suppress_until.write() = None;
                return Err(err);
            }
            tokio::time::sleep(Duration::from_millis(50)).await;
            self.paste_handler.simulate_paste()?;

            match self.repository.touch_last_used_at(item.id).await {
                Ok(()) => {
                    if let Some(callback) = self.on_change_callback.read().as_ref() {
                        callback();
                    }
                }
                Err(e) => tracing::warn!("Failed to update last_used_at: {}", e),
            }
        }

        Ok(())
    }

    pub async fn delete_item(&self, id: Uuid) -> Result<()> {
        self.repository.delete(id).await
    }

    pub async fn clear_history(&self) -> Result<()> {
        self.repository.clear().await
    }

    pub async fn search(&self, query: &str) -> Result<Vec<ClipboardItem>> {
        self.repository.search(query, 100).await
    }

    pub async fn set_pinned(&self, id: Uuid, pinned: bool) -> Result<()> {
        self.repository.set_pinned(id, pinned).await
    }

    pub fn get_settings(&self) -> Settings {
        self.settings.read().clone()
    }

    pub fn update_settings(&self, settings: Settings) {
        *self.settings.write() = settings;
    }

    pub async fn import_items(&self, items: Vec<ClipboardItem>) -> Result<usize> {
        let max_items = self.settings.read().max_history_items;
        let mut count = 0;

        for item in items {
            if let Err(e) = self.repository.save(&item).await {
                tracing::warn!("Failed to save imported clipboard item: {}", e);
            } else {
                count += 1;
            }
        }

        if let Err(e) = self.repository.prune(max_items).await {
            tracing::warn!("Failed to prune history after import: {}", e);
        }

        Ok(count)
    }

    pub fn set_on_change<F>(&self, callback: F)
    where
        F: Fn() + Send + Sync + 'static,
    {
        *self.on_change_callback.write() = Some(Box::new(callback));
    }
}
