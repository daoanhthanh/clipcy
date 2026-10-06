use std::sync::Arc;
use std::time::{Duration, Instant};

use parking_lot::RwLock;
use tokio::runtime::Handle;
use tokio::sync::{mpsc, oneshot};
use uuid::Uuid;

use crate::domain::{ClipboardItem, Settings};
use crate::error::Result;
use crate::ports::{ClipboardMonitor, ClipboardRepository, PasteHandler};

const DEBOUNCE_DURATION_MS: u64 = 300;
const PASTE_SUPPRESS_DURATION_MS: u64 = 1000;

enum DebounceMessage {
    Item(ClipboardItem),
    Flush(oneshot::Sender<Result<()>>),
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
            preview_len = item.preview.len(),
            thumbnail_len = item.thumbnail.as_ref().map(|t| t.len()).unwrap_or(0),
            source_app = item.source_app.as_deref(),
            "Saving clipboard item"
        );
        repository.save(&item).await?;
        let pruned = repository.prune(max_items).await?;
        tracing::debug!(
            id = %item.id,
            max_items,
            pruned,
            "Clipboard item saved and pruned"
        );
        
        // Notify UI that history changed
        if let Some(callback) = on_change_callback.read().as_ref() {
            callback();
        }
    }
    Ok(())
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
        }
    }

    pub fn start_monitoring(&self) -> Result<()> {
        tracing::info!("Starting clipboard monitoring");
        let (tx, rx) = mpsc::unbounded_channel::<DebounceMessage>();
        *self.debounce_tx.write() = Some(tx.clone());

        let repository = Arc::clone(&self.repository);
        let settings = Arc::clone(&self.settings);
        let suppress_until = Arc::clone(&self.suppress_until);
        let on_change_callback = Arc::clone(&self.on_change_callback);

        self.spawn_debounce_task(rx, repository, settings, suppress_until, on_change_callback);

        let debounce_tx = Arc::clone(&self.debounce_tx);
        self.monitor.on_change(Box::new(move |item| {
            if let Some(tx) = debounce_tx.read().as_ref() {
                let _ = tx.send(DebounceMessage::Item(item));
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
    ) {
        self.runtime_handle.spawn(async move {
            let debounce_duration = Duration::from_millis(DEBOUNCE_DURATION_MS);
            let mut pending_item: Option<ClipboardItem> = None;

            loop {
                let timeout = if pending_item.is_some() {
                    tokio::time::timeout(debounce_duration, rx.recv())
                } else {
                    tokio::time::timeout(Duration::from_secs(86400), rx.recv())
                };

                match timeout.await {
                    Ok(Some(message)) => match message {
                        DebounceMessage::Item(item) => {
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

                            tracing::debug!(
                                id = %item.id,
                                content_type = ?item.content_type,
                                data_len = item.data.len(),
                                preview_len = item.preview.len(),
                                thumbnail_len = item.thumbnail.as_ref().map(|t| t.len()).unwrap_or(0),
                                source_app = item.source_app.as_deref(),
                                "Clipboard item queued"
                            );
                            pending_item = Some(item);
                        }
                        DebounceMessage::Flush(response) => {
                            tracing::debug!("Flush requested for pending clipboard item");
                            let result =
                                save_pending_item(&mut pending_item, &repository, &settings, &on_change_callback).await;
                            let _ = response.send(result);
                        }
                    },
                    Ok(None) => {
                        break;
                    }
                    Err(_) => {
                        if let Err(e) =
                            save_pending_item(&mut pending_item, &repository, &settings, &on_change_callback).await
                        {
                            tracing::error!("Failed to save clipboard item: {}", e);
                        }
                    }
                }
            }
        });
    }

    pub fn stop_monitoring(&self) -> Result<()> {
        *self.debounce_tx.write() = None;
        self.monitor.stop()
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

        // Prune to maintain max items limit
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
