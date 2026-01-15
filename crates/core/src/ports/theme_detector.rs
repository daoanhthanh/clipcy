use crate::domain::Theme;
use crate::error::Result;

/// Callback type for theme changes
pub type ThemeCallback = Box<dyn Fn(Theme) + Send + Sync>;

/// Platform-specific system theme detection
pub trait ThemeDetector: Send + Sync {
    /// Get current system theme
    fn get_current(&self) -> Result<Theme>;

    /// Start observing theme changes
    fn observe(&self, callback: ThemeCallback) -> Result<()>;

    /// Stop observing theme changes
    fn stop_observing(&self) -> Result<()>;
}
