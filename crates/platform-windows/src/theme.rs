use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::thread;

use parking_lot::RwLock;
use windows::UI::ViewManagement::{UIColorType, UISettings};

use clipboard_core::domain::Theme;
use clipboard_core::error::{ClipboardError, Result};
use clipboard_core::ports::{ThemeCallback, ThemeDetector};

/// Windows theme detector using UISettings
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

    fn detect_theme() -> Result<Theme> {
        let settings = UISettings::new()
            .map_err(|e| ClipboardError::ThemeDetection(e.to_string()))?;

        let foreground = settings.GetColorValue(UIColorType::Foreground)
            .map_err(|e| ClipboardError::ThemeDetection(e.to_string()))?;

        // If foreground is light (white-ish), system is in dark mode
        let brightness = (foreground.R as u32 + foreground.G as u32 + foreground.B as u32) / 3;

        Ok(if brightness > 127 {
            Theme::Dark
        } else {
            Theme::Light
        })
    }
}

impl Default for WindowsThemeDetector {
    fn default() -> Self {
        Self::new()
    }
}

impl ThemeDetector for WindowsThemeDetector {
    fn get_current(&self) -> Result<Theme> {
        Self::detect_theme()
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
            let mut last_theme = Self::detect_theme().unwrap_or(Theme::Light);

            while running.load(Ordering::SeqCst) {
                if let Ok(current) = Self::detect_theme() {
                    if current != last_theme {
                        last_theme = current;
                        if let Some(cb) = callback.read().as_ref() {
                            cb(current);
                        }
                    }
                }

                thread::sleep(std::time::Duration::from_secs(1));
            }
        });

        Ok(())
    }

    fn stop_observing(&self) -> Result<()> {
        self.running.store(false, Ordering::SeqCst);
        Ok(())
    }
}
