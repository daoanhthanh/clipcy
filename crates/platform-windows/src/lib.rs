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
pub use paste::{save_foreground_window, WindowsPasteHandler};
#[cfg(target_os = "windows")]
pub use theme::WindowsThemeDetector;

/// Close the settings window if it's open
#[cfg(target_os = "windows")]
pub fn close_settings_window() {
    use windows::core::w;
    use windows::Win32::Foundation::{LPARAM, WPARAM};
    use windows::Win32::UI::WindowsAndMessaging::{FindWindowW, PostMessageW, WM_CLOSE};

    unsafe {
        if let Ok(hwnd) = FindWindowW(None, w!("Clipcy Settings")) {
            if !hwnd.is_invalid() {
                let _ = PostMessageW(Some(hwnd), WM_CLOSE, WPARAM(0), LPARAM(0));
                tracing::info!("Sent close message to settings window");
            }
        }
    }
}
