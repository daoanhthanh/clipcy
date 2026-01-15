// Only hide console window in release builds - allows seeing logs in debug mode
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::sync::Arc;

use clipboard_core::application::{AppController, ClipboardService};
use clipboard_core::domain::Settings;
use clipboard_core::ports::AutostartHandler;
use clipboard_storage::SqliteClipboardRepository;
use clipcy_platform_windows::ui::WindowsUIHandler;
use clipcy_platform_windows::{
    WindowsAutostartHandler, WindowsClipboardMonitor, WindowsHotkeyHandler, WindowsPasteHandler,
    WindowsThemeDetector,
};

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    // Use RUST_LOG env var if set, otherwise use default filter
    // Run with RUST_LOG=debug for more verbose output
    let filter = std::env::var("RUST_LOG")
        .unwrap_or_else(|_| "clipcy=debug,clipboard_core=debug,clipcy_platform_windows=debug,clipboard_storage=info".to_string());

    tracing_subscriber::fmt()
        .with_env_filter(filter)
        .init();

    tracing::info!("Starting Clipcy...");

    // Enable auto-start on Windows startup
    let autostart_handler = WindowsAutostartHandler::new();
    if !autostart_handler.is_enabled().unwrap_or(false) {
        if let Err(e) = autostart_handler.enable() {
            tracing::warn!("Failed to enable auto-start: {}", e);
        }
    }

    let repository = Arc::new(SqliteClipboardRepository::default_location().await?);

    let monitor = Arc::new(WindowsClipboardMonitor::new());
    let paste_handler = Arc::new(WindowsPasteHandler::new());
    let hotkey_handler = Arc::new(WindowsHotkeyHandler::new());
    let theme_detector = Arc::new(WindowsThemeDetector::new());
    let ui_handler = Arc::new(WindowsUIHandler::new());

    let clipboard_service = Arc::new(ClipboardService::new(
        monitor,
        repository,
        paste_handler,
        Settings::default(),
    ));

    let controller = AppController::new(
        clipboard_service,
        hotkey_handler,
        theme_detector,
        ui_handler,
    );

    controller.start().await?;

    let settings = Settings::default();
    tracing::info!("Clipcy started. Press {} to show popup.", settings.hotkey);

    loop {
        tokio::time::sleep(tokio::time::Duration::from_secs(1)).await;
    }
}
