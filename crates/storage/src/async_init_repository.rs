use std::path::PathBuf;
use std::sync::Arc;
use std::time::Duration;

use async_trait::async_trait;
use parking_lot::RwLock;
use tokio::runtime::Handle;
use uuid::Uuid;

use clipboard_core::domain::ClipboardItem;
use clipboard_core::error::{ClipboardError, Result};
use clipboard_core::ports::ClipboardRepository;

use crate::SqliteClipboardRepository;

/// Repository wrapper that initializes asynchronously in the background
///
/// This allows the application to start instantly without blocking on database
/// initialization. The database connection is established in a background task,
/// and operations wait (briefly) if initialization hasn't completed yet.
pub struct AsyncInitRepository {
    inner: Arc<RwLock<Option<Arc<SqliteClipboardRepository>>>>,
    init_started: Arc<parking_lot::Mutex<bool>>,
}

impl AsyncInitRepository {
    /// Create a new async-initializing repository
    ///
    /// This returns immediately without blocking. Database initialization
    /// starts in the background via the provided runtime handle.
    pub fn new(db_path: PathBuf, runtime_handle: Handle) -> Self {
        let repo = Self {
            inner: Arc::new(RwLock::new(None)),
            init_started: Arc::new(parking_lot::Mutex::new(false)),
        };

        // Start initialization in background immediately
        repo.start_init_async(db_path, runtime_handle);

        repo
    }

    /// Start database initialization in background task
    fn start_init_async(&self, db_path: PathBuf, runtime_handle: Handle) {
        let inner = self.inner.clone();
        let init_started = self.init_started.clone();

        runtime_handle.spawn(async move {
            *init_started.lock() = true;
            tracing::info!("Starting async database initialization at {:?}", db_path);

            match SqliteClipboardRepository::new(db_path).await {
                Ok(repo) => {
                    *inner.write() = Some(Arc::new(repo));
                    tracing::info!("Database initialization complete");
                }
                Err(e) => {
                    tracing::error!("Failed to initialize database: {}", e);
                    // Keep inner as None - operations will fail gracefully
                }
            }
        });
    }

    /// Wait for repository to be initialized (with timeout)
    async fn get_repo(&self) -> Result<Arc<SqliteClipboardRepository>> {
        // Fast path: already initialized
        if let Some(repo) = self.inner.read().as_ref() {
            return Ok(repo.clone());
        }

        // Wait for initialization (typically completes in background before first use)
        let max_wait = Duration::from_secs(10); // Generous timeout
        let start = std::time::Instant::now();
        let check_interval = Duration::from_millis(10);

        loop {
            if let Some(repo) = self.inner.read().as_ref() {
                let elapsed = start.elapsed();
                if elapsed > Duration::from_millis(100) {
                    tracing::warn!(
                        "Database initialization took {:?} (waited for completion)",
                        elapsed
                    );
                }
                return Ok(repo.clone());
            }

            if start.elapsed() > max_wait {
                return Err(ClipboardError::Storage(
                    "Database initialization timeout".to_string(),
                ));
            }

            tokio::time::sleep(check_interval).await;
        }
    }

    /// Check if initialization is complete
    pub fn is_initialized(&self) -> bool {
        self.inner.read().is_some()
    }

    /// Check if initialization has started
    pub fn init_started(&self) -> bool {
        *self.init_started.lock()
    }

    /// Get all items with metadata only (no full data payload)
    /// This is more efficient for listing
    pub async fn get_all_metadata(&self, limit: usize) -> Result<Vec<ClipboardItem>> {
        let repo = self.get_repo().await?;
        repo.get_all_metadata(limit).await
    }

    /// Search items by preview with metadata only
    pub async fn search_metadata(&self, query: &str, limit: usize) -> Result<Vec<ClipboardItem>> {
        let repo = self.get_repo().await?;
        repo.search_metadata(query, limit).await
    }
}

#[async_trait]
impl ClipboardRepository for AsyncInitRepository {
    async fn save(&self, item: &ClipboardItem) -> Result<()> {
        let repo = self.get_repo().await?;
        repo.save(item).await
    }

    async fn get_by_id(&self, id: Uuid) -> Result<Option<ClipboardItem>> {
        let repo = self.get_repo().await?;
        repo.get_by_id(id).await
    }

    async fn get_all(&self, limit: usize) -> Result<Vec<ClipboardItem>> {
        let repo = self.get_repo().await?;
        repo.get_all(limit).await
    }

    async fn delete(&self, id: Uuid) -> Result<()> {
        let repo = self.get_repo().await?;
        repo.delete(id).await
    }

    async fn clear(&self) -> Result<()> {
        let repo = self.get_repo().await?;
        repo.clear().await
    }

    async fn search(&self, query: &str, limit: usize) -> Result<Vec<ClipboardItem>> {
        let repo = self.get_repo().await?;
        repo.search(query, limit).await
    }

    async fn count(&self) -> Result<usize> {
        let repo = self.get_repo().await?;
        repo.count().await
    }

    async fn prune(&self, max_items: usize) -> Result<usize> {
        let repo = self.get_repo().await?;
        repo.prune(max_items).await
    }

    async fn set_pinned(&self, id: Uuid, pinned: bool) -> Result<()> {
        let repo = self.get_repo().await?;
        repo.set_pinned(id, pinned).await
    }
}

