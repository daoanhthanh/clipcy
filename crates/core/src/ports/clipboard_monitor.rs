use crate::domain::ClipboardItem;
use crate::error::Result;

/// Callback type for clipboard changes
pub type ClipboardCallback = Box<dyn Fn(ClipboardItem) + Send + Sync>;

/// Platform-specific clipboard monitoring
pub trait ClipboardMonitor: Send + Sync {
    /// Start monitoring clipboard changes
    fn start(&self) -> Result<()>;

    /// Stop monitoring
    fn stop(&self) -> Result<()>;

    /// Check if currently monitoring
    fn is_running(&self) -> bool;

    /// Set callback for clipboard changes
    fn on_change(&self, callback: ClipboardCallback);

    /// Get current clipboard content (one-shot read)
    fn get_current(&self) -> Result<Option<ClipboardItem>>;

    /// Set clipboard content
    fn set_content(&self, item: &ClipboardItem) -> Result<()>;
}
