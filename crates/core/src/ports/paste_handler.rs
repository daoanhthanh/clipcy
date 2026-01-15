use crate::error::Result;

/// Platform-specific paste simulation
pub trait PasteHandler: Send + Sync {
    /// Simulate Ctrl+V / Cmd+V paste action
    fn simulate_paste(&self) -> Result<()>;

    /// Simulate paste with delay (for apps that need it)
    fn simulate_paste_delayed(&self, delay_ms: u64) -> Result<()>;
}
