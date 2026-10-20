use async_trait::async_trait;
use uuid::Uuid;

use crate::domain::ClipboardItem;
use crate::error::Result;

/// Repository for clipboard history persistence
#[async_trait]
pub trait ClipboardRepository: Send + Sync {
    /// Save a clipboard item
    async fn save(&self, item: &ClipboardItem) -> Result<()>;

    /// Get item by ID
    async fn get_by_id(&self, id: Uuid) -> Result<Option<ClipboardItem>>;

    /// Get all pinned items followed by up to limit unpinned items, newest first in each group.
    async fn get_all(&self, limit: usize) -> Result<Vec<ClipboardItem>>;

    /// Update last-used timestamp for an item
    async fn touch_last_used_at(&self, id: Uuid) -> Result<()>;

    /// Delete item by ID
    async fn delete(&self, id: Uuid) -> Result<()>;

    /// Delete all non-pinned items
    async fn clear(&self) -> Result<()>;

    /// Search items by preview text
    async fn search(&self, query: &str, limit: usize) -> Result<Vec<ClipboardItem>>;

    /// Get total count of items
    async fn count(&self) -> Result<usize>;

    /// Keep at most max_items unpinned items; never prune pinned items.
    async fn prune(&self, max_items: usize) -> Result<usize>;

    /// Update pinned status
    async fn set_pinned(&self, id: Uuid, pinned: bool) -> Result<()>;
}
