use crate::domain::ClipboardItem;
use crate::error::Result;

/// A clipboard change decoded under one capture epoch.
///
/// `sequence` is an equality token only. Zero means the platform could not
/// supply one. Callers must not infer elapsed time or missed events from it.
#[derive(Debug, Clone)]
pub struct ClipboardObservation {
    pub epoch: u64,
    pub sequence: u32,
    pub item: ClipboardItem,
}

/// Callback type for clipboard changes.
pub type ClipboardCallback = Box<dyn Fn(ClipboardObservation) + Send + Sync>;

/// Platform-specific clipboard monitoring.
pub trait ClipboardMonitor: Send + Sync {
    /// Start monitoring clipboard changes.
    fn start(&self) -> Result<()>;

    /// Stop monitoring.
    fn stop(&self) -> Result<()>;

    /// Check if currently monitoring.
    fn is_running(&self) -> bool;

    /// Set callback for clipboard changes.
    fn on_change(&self, callback: ClipboardCallback);

    /// Get current clipboard content (one-shot read).
    fn get_current(&self) -> Result<Option<ClipboardItem>>;

    /// Set clipboard content.
    fn set_content(&self, item: &ClipboardItem) -> Result<()>;

    /// Update whether the platform's clipboard-monitor exclusion marker is honored.
    ///
    /// Applies synchronously to future reads without importing or deleting content.
    fn set_respect_clipboard_exclusion(&self, respect: bool);

    /// Read the current clipboard sequence without decoding contents.
    ///
    /// Zero is treated as unavailable and returned as an error.
    fn sample_sequence(&self) -> Result<u32>;

    /// Publish the capture gate.
    ///
    /// Pause disables new payload decode immediately. Resume publishes
    /// `resume_baseline` before capture is reopened. A zero baseline is rejected
    /// and must leave capture paused. `None` means no baseline (initial start).
    fn apply_capture_policy(
        &self,
        paused: bool,
        epoch: u64,
        resume_baseline: Option<u32>,
    ) -> Result<()>;
}
