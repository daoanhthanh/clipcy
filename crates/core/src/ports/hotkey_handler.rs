use crate::error::Result;

/// Callback type for hotkey triggers
pub type HotkeyCallback = Box<dyn Fn() + Send + Sync>;

/// Platform-specific global hotkey registration
pub trait HotkeyHandler: Send + Sync {
    /// Register a global hotkey
    /// Returns a handle ID for later unregistration
    fn register(&self, hotkey: &str, callback: HotkeyCallback) -> Result<u32>;

    /// Unregister a hotkey by handle
    fn unregister(&self, handle: u32) -> Result<()>;

    /// Unregister all hotkeys
    fn unregister_all(&self) -> Result<()>;
}
