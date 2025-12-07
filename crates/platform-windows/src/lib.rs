//! Platform-specific implementations for Windows.
//!
//! This crate provides Windows-specific handlers for:
//! - Clipboard monitoring
//! - Global hotkeys
//! - Paste simulation
//! - Theme detection
//! - Autostart management
//!
//! Note: UI is now handled by the native WinUI 3 application (apps/clipcy-windows-ui).

#[cfg(target_os = "windows")]
mod autostart;
#[cfg(target_os = "windows")]
mod clipboard;
#[cfg(target_os = "windows")]
mod hotkey;
#[cfg(target_os = "windows")]
mod paste;
#[cfg(target_os = "windows")]
mod theme;

#[cfg(target_os = "windows")]
pub use autostart::WindowsAutostartHandler;
#[cfg(target_os = "windows")]
pub use clipboard::WindowsClipboardMonitor;
#[cfg(target_os = "windows")]
pub use hotkey::WindowsHotkeyHandler;
#[cfg(target_os = "windows")]
pub use paste::{save_foreground_window, WindowsPasteHandler};
#[cfg(target_os = "windows")]
pub use theme::WindowsThemeDetector;
