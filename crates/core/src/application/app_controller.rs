use parking_lot::RwLock;
use std::sync::Arc;
use tokio::runtime::Handle;

use crate::application::ClipboardService;
use crate::domain::Theme;
use crate::error::Result;
use crate::ports::{HotkeyHandler, ThemeDetector, UIEvent, UIHandler};

/// Main application controller - orchestrates all services
pub struct AppController {
    clipboard_service: Arc<ClipboardService>,
    hotkey_handler: Arc<dyn HotkeyHandler>,
    theme_detector: Arc<dyn ThemeDetector>,
    ui_handler: Arc<dyn UIHandler>,
    current_theme: Arc<RwLock<Theme>>,
    hotkey_handle: Arc<RwLock<Option<u32>>>,
}

impl AppController {
    pub fn new(
        clipboard_service: Arc<ClipboardService>,
        hotkey_handler: Arc<dyn HotkeyHandler>,
        theme_detector: Arc<dyn ThemeDetector>,
        ui_handler: Arc<dyn UIHandler>,
    ) -> Self {
        Self {
            clipboard_service,
            hotkey_handler,
            theme_detector,
            ui_handler,
            current_theme: Arc::new(RwLock::new(Theme::default())),
            hotkey_handle: Arc::new(RwLock::new(None)),
        }
    }

    /// Initialize and start the application
    pub async fn start(&self) -> Result<()> {
        // Start clipboard monitoring
        self.clipboard_service.start_monitoring()?;

        // Register hotkey (capture runtime handle for callbacks from non-tokio threads)
        let runtime_handle = Handle::current();
        self.register_hotkey(runtime_handle.clone())?;

        // Setup theme detection
        self.setup_theme_detection()?;

        // Setup UI event handling
        self.setup_ui_events(runtime_handle);

        // Show tray icon
        self.ui_handler.show_tray()?;

        tracing::info!("Application started successfully");
        Ok(())
    }

    /// Stop the application
    pub async fn stop(&self) -> Result<()> {
        self.clipboard_service.stop_monitoring()?;
        self.hotkey_handler.unregister_all()?;
        self.theme_detector.stop_observing()?;
        self.ui_handler.hide_tray()?;

        tracing::info!("Application stopped");
        Ok(())
    }

    fn register_hotkey(&self, runtime_handle: Handle) -> Result<()> {
        let settings = self.clipboard_service.get_settings();
        let ui = Arc::clone(&self.ui_handler);
        let clipboard = Arc::clone(&self.clipboard_service);

        let handle = self.hotkey_handler.register(
            &settings.hotkey,
            Box::new(move || {
                tracing::info!("Hotkey callback triggered!");
                let ui = Arc::clone(&ui);
                let clipboard = Arc::clone(&clipboard);

                // Use the captured runtime handle to spawn on tokio from non-tokio thread
                runtime_handle.spawn(async move {
                    tracing::debug!("Async task started, fetching clipboard history...");
                    match clipboard.get_history(Some(20)).await {
                        Ok(items) => {
                            if let Err(e) = ui.show_popup(&items) {
                                tracing::error!("Failed to show popup: {}", e);
                            }
                        }
                        Err(e) => {
                            tracing::error!("Failed to get history: {}", e);
                        }
                    }
                });
            }),
        )?;

        *self.hotkey_handle.write() = Some(handle);
        Ok(())
    }

    fn setup_theme_detection(&self) -> Result<()> {
        // Get initial theme
        let theme = self.theme_detector.get_current()?;
        *self.current_theme.write() = theme;
        self.ui_handler.set_theme(theme)?;

        // Observe changes
        let ui = Arc::clone(&self.ui_handler);
        let current = Arc::clone(&self.current_theme);

        self.theme_detector.observe(Box::new(move |new_theme| {
            *current.write() = new_theme;
            if let Err(e) = ui.set_theme(new_theme) {
                tracing::error!("Failed to update theme: {}", e);
            }
        }))?;

        Ok(())
    }

    fn setup_ui_events(&self, runtime_handle: Handle) {
        let clipboard = Arc::clone(&self.clipboard_service);
        let ui = Arc::clone(&self.ui_handler);

        self.ui_handler.on_event(Box::new(move |event| {
            let clipboard = Arc::clone(&clipboard);
            let ui = Arc::clone(&ui);

            // Use the captured runtime handle to spawn on tokio from non-tokio thread
            runtime_handle.spawn(async move {
                match event {
                    UIEvent::ItemSelected(index) => {
                        if let Ok(items) = clipboard.get_history(Some(20)).await {
                            if let Some(item) = items.get(index) {
                                if let Err(e) = clipboard.paste_item(item.id).await {
                                    tracing::error!("Failed to paste: {}", e);
                                }
                            }
                        }
                        let _ = ui.hide_popup();
                    }
                    UIEvent::ClearHistoryClicked => {
                        if let Err(e) = clipboard.clear_history().await {
                            tracing::error!("Failed to clear history: {}", e);
                        }
                    }
                    UIEvent::SettingsClicked => {
                        // TODO: Open settings window
                        tracing::info!("Settings clicked");
                    }
                    UIEvent::QuitClicked => {
                        std::process::exit(0);
                    }
                    UIEvent::SearchChanged(query) => {
                        match clipboard.search(&query).await {
                            Ok(items) => {
                                if let Err(e) = ui.show_popup(&items) {
                                    tracing::error!("Failed to update popup: {}", e);
                                }
                            }
                            Err(e) => {
                                tracing::error!("Search failed: {}", e);
                            }
                        }
                    }
                }
            });
        }));
    }
}
