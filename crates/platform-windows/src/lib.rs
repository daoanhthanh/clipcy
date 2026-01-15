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
pub mod ui;

#[cfg(target_os = "windows")]
pub use autostart::WindowsAutostartHandler;
#[cfg(target_os = "windows")]
pub use clipboard::WindowsClipboardMonitor;
#[cfg(target_os = "windows")]
pub use hotkey::WindowsHotkeyHandler;
#[cfg(target_os = "windows")]
pub use paste::WindowsPasteHandler;
#[cfg(target_os = "windows")]
pub use theme::WindowsThemeDetector;
