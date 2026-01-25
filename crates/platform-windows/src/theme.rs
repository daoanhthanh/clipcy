use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::thread;

use parking_lot::RwLock;
use windows::Win32::Foundation::ERROR_SUCCESS;
use windows::Win32::System::Registry::{
    RegCloseKey, RegNotifyChangeKeyValue, RegOpenKeyExW, RegQueryValueExW,
    HKEY, HKEY_CURRENT_USER, KEY_NOTIFY, KEY_READ, REG_NOTIFY_CHANGE_LAST_SET, REG_VALUE_TYPE,
};
use windows::core::PCWSTR;

use clipboard_core::domain::Theme;
use clipboard_core::error::{ClipboardError, Result};
use clipboard_core::ports::{ThemeCallback, ThemeDetector};

/// Registry path for theme detection
const PERSONALIZE_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
const APPS_USE_LIGHT_THEME: &str = "AppsUseLightTheme";

/// Windows theme detector using Registry notifications (zero-polling)
/// This provides instant theme change detection (~1ms latency)
pub struct WindowsThemeDetector {
    callback: Arc<RwLock<Option<ThemeCallback>>>,
    running: Arc<AtomicBool>,
}

impl WindowsThemeDetector {
    pub fn new() -> Self {
        Self {
            callback: Arc::new(RwLock::new(None)),
            running: Arc::new(AtomicBool::new(false)),
        }
    }

    /// Read theme directly from registry (fastest method, ~1ms)
    fn detect_theme_from_registry() -> Result<Theme> {
        unsafe {
            let key_path: Vec<u16> = PERSONALIZE_KEY
                .encode_utf16()
                .chain(std::iter::once(0))
                .collect();
            
            let mut hkey = HKEY::default();
            let result = RegOpenKeyExW(
                HKEY_CURRENT_USER,
                PCWSTR(key_path.as_ptr()),
                Some(0),
                KEY_READ,
                &mut hkey,
            );
            
            if result != ERROR_SUCCESS {
                return Err(ClipboardError::ThemeDetection(
                    format!("Failed to open registry key: {:?}", result)
                ));
            }
            
            let value_name: Vec<u16> = APPS_USE_LIGHT_THEME
                .encode_utf16()
                .chain(std::iter::once(0))
                .collect();
            
            let mut data: u32 = 0;
            let mut data_size: u32 = std::mem::size_of::<u32>() as u32;
            let mut data_type: REG_VALUE_TYPE = REG_VALUE_TYPE::default();
            
            let result = RegQueryValueExW(
                hkey,
                PCWSTR(value_name.as_ptr()),
                None,
                Some(&mut data_type),
                Some(&mut data as *mut u32 as *mut u8),
                Some(&mut data_size),
            );
            
            let _ = RegCloseKey(hkey);
            
            if result != ERROR_SUCCESS {
                return Err(ClipboardError::ThemeDetection(
                    format!("Failed to query registry value: {:?}", result)
                ));
            }
            
            // AppsUseLightTheme: 0 = Dark, 1 = Light
            Ok(if data == 0 { Theme::Dark } else { Theme::Light })
        }
    }

    /// Watch registry for theme changes using RegNotifyChangeKeyValue
    /// Uses a separate notify thread to avoid blocking the callback
    fn watch_registry(callback: Arc<RwLock<Option<ThemeCallback>>>, running: Arc<AtomicBool>) {
        unsafe {
            let key_path: Vec<u16> = PERSONALIZE_KEY
                .encode_utf16()
                .chain(std::iter::once(0))
                .collect();
            
            let mut last_theme = Self::detect_theme_from_registry().unwrap_or(Theme::Light);
            
            while running.load(Ordering::SeqCst) {
                let mut hkey = HKEY::default();
                let result = RegOpenKeyExW(
                    HKEY_CURRENT_USER,
                    PCWSTR(key_path.as_ptr()),
                    Some(0),
                    KEY_NOTIFY,
                    &mut hkey,
                );
                
                if result != ERROR_SUCCESS {
                    thread::sleep(std::time::Duration::from_secs(5));
                    continue;
                }
                
                // Wait for the registry key to change
                let result = RegNotifyChangeKeyValue(
                    hkey,
                    false,
                    REG_NOTIFY_CHANGE_LAST_SET,
                    None,
                    false, // Synchronous - blocks until change
                );
                
                let _ = RegCloseKey(hkey);
                
                if result != ERROR_SUCCESS {
                    continue;
                }
                
                // Check if we're still running
                if !running.load(Ordering::SeqCst) {
                    break;
                }
                
                // Read new theme and fire callback on a separate thread
                // to avoid blocking the registry watcher
                if let Ok(current) = Self::detect_theme_from_registry() {
                    if current != last_theme {
                        last_theme = current;
                        
                        // Clone callback and spawn a new thread for notification
                        // This ensures the registry watcher doesn't block on C# callback
                        let cb_ref = callback.clone();
                        let theme_copy = current;
                        thread::spawn(move || {
                            if let Some(cb) = cb_ref.read().as_ref() {
                                cb(theme_copy);
                            }
                        });
                    }
                }
            }
        }
    }
}

impl Default for WindowsThemeDetector {
    fn default() -> Self {
        Self::new()
    }
}

impl ThemeDetector for WindowsThemeDetector {
    fn get_current(&self) -> Result<Theme> {
        Self::detect_theme_from_registry()
    }

    fn observe(&self, callback: ThemeCallback) -> Result<()> {
        *self.callback.write() = Some(callback);

        if self.running.load(Ordering::SeqCst) {
            return Ok(());
        }

        self.running.store(true, Ordering::SeqCst);

        let callback = Arc::clone(&self.callback);
        let running = Arc::clone(&self.running);

        thread::spawn(move || {
            Self::watch_registry(callback, running);
        });

        Ok(())
    }

    fn stop_observing(&self) -> Result<()> {
        self.running.store(false, Ordering::SeqCst);
        Ok(())
    }
}
