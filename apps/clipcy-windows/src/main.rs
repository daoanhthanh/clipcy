// Only hide console window in release builds - allows seeing logs in debug mode
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::sync::Arc;
use std::time::Duration;

use clipboard_core::application::{AppController, ClipboardService};
use clipboard_core::ports::{AutostartHandler, UIHandler};
use clipboard_storage::SqliteClipboardRepository;
use clipcy_platform_windows::ui::WindowsUIHandler;
use clipcy_platform_windows::{
    WindowsAutostartHandler, WindowsClipboardMonitor, WindowsHotkeyHandler, WindowsPasteHandler,
    WindowsThemeDetector,
};
use clipcy_settings_common::SettingsManager;
use notify::{Config, RecommendedWatcher, RecursiveMode, Watcher};
use tracing_subscriber::layer::SubscriberExt;
use tracing_subscriber::util::SubscriberInitExt;

/// Initialize logging based on build mode:
/// - Debug: logs to console only
/// - Release: logs to file in %LOCALAPPDATA%/clipcy/logs/
fn init_logging() -> Option<tracing_appender::non_blocking::WorkerGuard> {
    let filter = std::env::var("RUST_LOG").unwrap_or_else(|_| {
        "clipcy=debug,clipboard_core=debug,clipcy_platform_windows=debug,clipboard_storage=info"
            .to_string()
    });

    let env_filter = tracing_subscriber::EnvFilter::new(&filter);

    #[cfg(debug_assertions)]
    {
        // Debug mode: console only
        tracing_subscriber::registry()
            .with(env_filter)
            .with(tracing_subscriber::fmt::layer())
            .init();
        None
    }

    #[cfg(not(debug_assertions))]
    {
        // Release mode: log to file
        let log_dir = dirs::data_local_dir()
            .unwrap_or_else(|| std::path::PathBuf::from("."))
            .join("clipcy")
            .join("logs");

        // Create log directory if it doesn't exist
        let _ = std::fs::create_dir_all(&log_dir);

        let file_appender = tracing_appender::rolling::daily(&log_dir, "clipcy.log");
        let (non_blocking, guard) = tracing_appender::non_blocking(file_appender);

        tracing_subscriber::registry()
            .with(env_filter)
            .with(
                tracing_subscriber::fmt::layer()
                    .with_writer(non_blocking)
                    .with_ansi(false),
            )
            .init();

        Some(guard)
    }
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    // Keep the guard alive for the lifetime of the application
    let _log_guard = init_logging();

    tracing::info!("Starting Clipcy...");

    // Load settings from JSON file
    let settings_manager = SettingsManager::new().expect("Failed to create settings manager");
    let mut settings = settings_manager.load_or_create().unwrap_or_default();
    tracing::info!("Loaded settings: hotkey={}", settings.hotkey);

    let repository = Arc::new(SqliteClipboardRepository::default_location().await?);

    let monitor = Arc::new(WindowsClipboardMonitor::new());
    let paste_handler = Arc::new(WindowsPasteHandler::new());
    let hotkey_handler = Arc::new(WindowsHotkeyHandler::new());
    let theme_detector = Arc::new(WindowsThemeDetector::new());
    let ui_handler = Arc::new(WindowsUIHandler::new());
    let autostart_handler = Arc::new(WindowsAutostartHandler::new());

    // Sync autostart state from registry to settings
    settings.start_on_boot = autostart_handler.is_enabled().unwrap_or(true);
    let _ = ui_handler.update_settings(&settings);

    let clipboard_service = Arc::new(ClipboardService::new(
        monitor,
        repository,
        paste_handler,
        settings.clone(),
    ));

    let controller = AppController::new(
        clipboard_service,
        hotkey_handler,
        theme_detector,
        ui_handler.clone(),
        autostart_handler.clone(),
    );

    controller.start().await?;

    tracing::info!("Clipcy started. Press {} to show popup.", settings.hotkey);

    // Setup file watcher for settings changes
    let ui_handler_watcher = ui_handler.clone();
    let autostart_handler_watcher = autostart_handler.clone();
    let settings_path = settings_manager.path().clone();

    let (tx, rx) = std::sync::mpsc::channel();

    let mut watcher = RecommendedWatcher::new(
        move |res| {
            if let Ok(event) = res {
                let _ = tx.send(event);
            }
        },
        Config::default().with_poll_interval(Duration::from_millis(500)),
    )?;

    // Watch the settings directory
    if let Some(parent) = settings_path.parent() {
        let _ = std::fs::create_dir_all(parent);
        watcher.watch(parent, RecursiveMode::NonRecursive)?;
        tracing::info!("Watching settings directory: {:?}", parent);
    }

    // Handle settings file changes in a separate task
    tokio::spawn(async move {
        let mut last_settings = settings;

        loop {
            // Check for file change events
            while let Ok(_event) = rx.try_recv() {
                // Debounce: wait a bit for file to be fully written
                tokio::time::sleep(Duration::from_millis(100)).await;

                // Reload settings
                if let Ok(new_settings) = SettingsManager::new()
                    .and_then(|m| m.load())
                {
                    if new_settings != last_settings {
                        tracing::info!("Settings changed, applying...");

                        // Apply settings to UI handler
                        if let Err(e) = ui_handler_watcher.update_settings(&new_settings) {
                            tracing::error!("Failed to update UI settings: {}", e);
                        }

                        // Handle autostart changes
                        if new_settings.start_on_boot != last_settings.start_on_boot {
                            let result = if new_settings.start_on_boot {
                                autostart_handler_watcher.enable()
                            } else {
                                autostart_handler_watcher.disable()
                            };
                            if let Err(e) = result {
                                tracing::error!("Failed to update autostart: {}", e);
                            } else {
                                tracing::info!("Autostart set to: {}", new_settings.start_on_boot);
                            }
                        }

                        // Log other changes
                        if new_settings.show_preview_panel != last_settings.show_preview_panel {
                            tracing::info!("Preview panel: {}", new_settings.show_preview_panel);
                        }
                        if new_settings.max_history_items != last_settings.max_history_items {
                            tracing::info!("Max history items: {}", new_settings.max_history_items);
                        }
                        if new_settings.hotkey != last_settings.hotkey {
                            tracing::info!("Hotkey changed to: {} (restart required)", new_settings.hotkey);
                        }

                        last_settings = new_settings;
                    }
                }
            }

            tokio::time::sleep(Duration::from_millis(200)).await;
        }
    });

    // Keep watcher alive
    let _watcher = watcher;

    loop {
        tokio::time::sleep(tokio::time::Duration::from_secs(1)).await;
    }
}
