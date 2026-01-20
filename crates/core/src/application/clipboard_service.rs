use std::sync::Arc;
use std::time::Duration;

use parking_lot::RwLock;
use tokio::runtime::Handle;
use tokio::sync::mpsc;
use uuid::Uuid;

use crate::domain::{ClipboardItem, Settings};
use crate::error::Result;
use crate::ports::{ClipboardMonitor, ClipboardRepository, PasteHandler};

const DEBOUNCE_DURATION_MS: u64 = 300;

pub struct ClipboardService {
    monitor: Arc<dyn ClipboardMonitor>,
    repository: Arc<dyn ClipboardRepository>,
    paste_handler: Arc<dyn PasteHandler>,
    settings: Arc<RwLock<Settings>>,
    debounce_tx: Arc<RwLock<Option<mpsc::UnboundedSender<ClipboardItem>>>>,
    runtime_handle: Handle,
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
            runtime_handle,
        }
    }

    pub fn start_monitoring(&self) -> Result<()> {
        let (tx, rx) = mpsc::unbounded_channel::<ClipboardItem>();
        *self.debounce_tx.write() = Some(tx.clone());

        let repository = Arc::clone(&self.repository);
        let settings = Arc::clone(&self.settings);

        self.spawn_debounce_task(rx, repository, settings);

        let debounce_tx = Arc::clone(&self.debounce_tx);
        self.monitor.on_change(Box::new(move |item| {
            if let Some(tx) = debounce_tx.read().as_ref() {
                let _ = tx.send(item);
            }
        }));

        self.monitor.start()
    }

    fn spawn_debounce_task(
        &self,
        mut rx: mpsc::UnboundedReceiver<ClipboardItem>,
        repository: Arc<dyn ClipboardRepository>,
        settings: Arc<RwLock<Settings>>,
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
                    Ok(Some(item)) => {
                        pending_item = Some(item);
                    }
                    Ok(None) => {
                        break;
                    }
                    Err(_) => {
                        if let Some(item) = pending_item.take() {
                            let max_items = settings.read().max_history_items;

                            if let Err(e) = repository.save(&item).await {
                                tracing::error!("Failed to save clipboard item: {}", e);
                            }

                            if let Err(e) = repository.prune(max_items).await {
                                tracing::error!("Failed to prune history: {}", e);
                            }
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

    pub async fn get_history(&self, limit: Option<usize>) -> Result<Vec<ClipboardItem>> {
        let limit = limit.unwrap_or(self.settings.read().max_history_items);
        self.repository.get_all(limit).await
    }

    pub async fn paste_item(&self, id: Uuid) -> Result<()> {
        let item = self.repository.get_by_id(id).await?;

        if let Some(item) = item {
            self.monitor.set_content(&item)?;
            tokio::time::sleep(Duration::from_millis(50)).await;
            self.paste_handler.simulate_paste()?;
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
        self.repository.search(query, 50).await
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
}
