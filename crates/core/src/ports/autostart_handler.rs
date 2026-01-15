use crate::error::Result;

/// Trait for handling application auto-start configuration
pub trait AutostartHandler: Send + Sync {
    /// Enable auto-start when the system starts
    fn enable(&self) -> Result<()>;

    /// Disable auto-start
    fn disable(&self) -> Result<()>;

    /// Check if auto-start is currently enabled
    fn is_enabled(&self) -> Result<bool>;
}
