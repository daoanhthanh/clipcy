// Only hide console window in release builds - allows seeing logs in debug mode
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::sync::Arc;

use clipboard_core::application::{AppController, ClipboardService};
use clipboard_core::domain::Settings;
use clipboard_core::ports::{AutostartHandler, UIHandler};
use clipboard_storage::SqliteClipboardRepository;
use clipcy_platform_windows::ui::WindowsUIHandler;
use clipcy_platform_windows::{
    WindowsAutostartHandler, WindowsClipboardMonitor, WindowsHotkeyHandler, WindowsPasteHandler,
    WindowsThemeDetector,
};
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

    let repository = Arc::new(SqliteClipboardRepository::default_location().await?);

    let monitor = Arc::new(WindowsClipboardMonitor::new());
    let paste_handler = Arc::new(WindowsPasteHandler::new());
    let hotkey_handler = Arc::new(WindowsHotkeyHandler::new());
    let theme_detector = Arc::new(WindowsThemeDetector::new());
    let ui_handler = Arc::new(WindowsUIHandler::new());
    let autostart_handler = Arc::new(WindowsAutostartHandler::new());

    // Initialize UI handler with current settings including autostart state
    let mut settings = Settings::default();
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
        ui_handler,
        autostart_handler,
    );

    controller.start().await?;

    tracing::info!("Clipcy started. Press {} to show popup.", settings.hotkey);

    loop {
        tokio::time::sleep(tokio::time::Duration::from_secs(1)).await;
    }
}
