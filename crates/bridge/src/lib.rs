//! Clipcy Bridge - UniFFI wrapper for native UI integration
//!
//! This crate exposes the Clipcy core functionality to native UI applications
//! via UniFFI-generated bindings (C#, Swift, Kotlin, etc.)

use std::path::PathBuf;
use std::sync::Arc;

use parking_lot::RwLock;
use tokio::runtime::Runtime;
use uuid::Uuid;

use clipboard_core::application::ClipboardService;
use clipboard_core::domain::{ClipboardItem, ContentType, Settings, Theme};
use clipboard_core::ports::{
    AutostartHandler, ClipboardMonitor, ClipboardRepository, HotkeyHandler, ThemeDetector, UIEvent,
    UIHandler,
};
use clipboard_storage::SqliteClipboardRepository;
use clipcy_settings_common::{SettingsManager, ThemeMode};

#[cfg(target_os = "windows")]
use clipcy_platform_windows::{
    WindowsAutostartHandler, WindowsClipboardMonitor, WindowsHotkeyHandler, WindowsPasteHandler,
    WindowsThemeDetector,
};

// Include the UniFFI scaffolding
uniffi::include_scaffolding!("clipcy_core");

// ============================================
// Data Transfer Objects
// ============================================

/// Content type enum for FFI
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ContentTypeDto {
    Text,
    Image,
    Files,
    Html,
    RichText,
}

impl From<ContentType> for ContentTypeDto {
    fn from(ct: ContentType) -> Self {
        match ct {
            ContentType::Text => ContentTypeDto::Text,
            ContentType::Image => ContentTypeDto::Image,
            ContentType::Files => ContentTypeDto::Files,
            ContentType::Html => ContentTypeDto::Html,
            ContentType::RichText => ContentTypeDto::RichText,
        }
    }
}

impl From<ContentTypeDto> for ContentType {
    fn from(ct: ContentTypeDto) -> Self {
        match ct {
            ContentTypeDto::Text => ContentType::Text,
            ContentTypeDto::Image => ContentType::Image,
            ContentTypeDto::Files => ContentType::Files,
            ContentTypeDto::Html => ContentType::Html,
            ContentTypeDto::RichText => ContentType::RichText,
        }
    }
}

/// Theme mode enum for FFI
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ThemeModeDto {
    Light,
    Dark,
    System,
}

impl From<ThemeMode> for ThemeModeDto {
    fn from(tm: ThemeMode) -> Self {
        match tm {
            ThemeMode::Light => ThemeModeDto::Light,
            ThemeMode::Dark => ThemeModeDto::Dark,
            ThemeMode::System => ThemeModeDto::System,
        }
    }
}

impl From<ThemeModeDto> for ThemeMode {
    fn from(tm: ThemeModeDto) -> Self {
        match tm {
            ThemeModeDto::Light => ThemeMode::Light,
            ThemeModeDto::Dark => ThemeMode::Dark,
            ThemeModeDto::System => ThemeMode::System,
        }
    }
}

/// Clipboard item DTO - flattened structure for FFI boundary
#[derive(Debug, Clone)]
pub struct ClipboardItemDto {
    pub id: String,
    pub content_type: ContentTypeDto,
    pub preview: String,
    pub thumbnail: Option<Vec<u8>>,
    pub source_app: Option<String>,
    pub created_at_timestamp: u64,
    pub pinned: bool,
}

impl From<ClipboardItem> for ClipboardItemDto {
    fn from(item: ClipboardItem) -> Self {
        Self {
            id: item.id.to_string(),
            content_type: item.content_type.into(),
            preview: item.preview,
            thumbnail: item.thumbnail,
            source_app: item.source_app,
            created_at_timestamp: item.created_at.timestamp() as u64,
            pinned: item.pinned,
        }
    }
}

/// Settings DTO for FFI
#[derive(Debug, Clone)]
pub struct SettingsDto {
    pub max_history_items: u32,
    pub hotkey: String,
    pub theme: ThemeModeDto,
    pub start_on_boot: bool,
    pub show_notifications: bool,
    pub show_preview_panel: bool,
    pub excluded_apps: Vec<String>,
}

impl From<clipcy_settings_common::Settings> for SettingsDto {
    fn from(s: clipcy_settings_common::Settings) -> Self {
        Self {
            max_history_items: s.max_history_items as u32,
            hotkey: s.hotkey,
            theme: s.theme.into(),
            start_on_boot: s.start_on_boot,
            show_notifications: s.show_notifications,
            show_preview_panel: s.show_preview_panel,
            excluded_apps: s.excluded_apps,
        }
    }
}

impl From<SettingsDto> for clipcy_settings_common::Settings {
    fn from(s: SettingsDto) -> Self {
        Self {
            max_history_items: s.max_history_items as usize,
            hotkey: s.hotkey,
            theme: s.theme.into(),
            start_on_boot: s.start_on_boot,
            show_notifications: s.show_notifications,
            show_preview_panel: s.show_preview_panel,
            excluded_apps: s.excluded_apps,
        }
    }
}

// ============================================
// Events sent to UI
// ============================================

/// Events sent from Rust to the native UI
#[derive(Debug, Clone)]
pub enum BridgeEvent {
    /// Clipboard history was updated (item added/deleted)
    HistoryUpdated,
    /// Global hotkey was triggered
    HotkeyTriggered,
    /// System theme changed
    ThemeChanged { is_dark: bool },
    /// New clipboard item captured
    ClipboardChanged { item: ClipboardItemDto },
    /// An error occurred
    Error { message: String },
}

// ============================================
// Callback Interface
// ============================================

/// Callback interface implemented by the native UI
pub trait UiCallback: Send + Sync {
    fn on_event(&self, event: BridgeEvent);
}

// ============================================
// Errors
// ============================================

/// Bridge-specific errors
#[derive(Debug, thiserror::Error)]
pub enum BridgeError {
    #[error("Storage error: {0}")]
    StorageError(String),
    #[error("Platform error: {0}")]
    PlatformError(String),
    #[error("Invalid ID format")]
    InvalidId,
    #[error("Engine not initialized")]
    NotInitialized,
    #[error("Engine already running")]
    AlreadyRunning,
}

impl From<clipboard_core::error::ClipboardError> for BridgeError {
    fn from(e: clipboard_core::error::ClipboardError) -> Self {
        BridgeError::StorageError(e.to_string())
    }
}

// ============================================
// Bridge UI Handler (forwards events to callback)
// ============================================

/// Internal UI handler that forwards events to the native callback
struct BridgeUiHandler {
    callback: Arc<RwLock<Option<Arc<dyn UiCallback>>>>,
}

impl BridgeUiHandler {
    fn new() -> Self {
        Self {
            callback: Arc::new(RwLock::new(None)),
        }
    }

    fn set_callback(&self, callback: Arc<dyn UiCallback>) {
        *self.callback.write() = Some(callback);
    }

    fn send_event(&self, event: BridgeEvent) {
        if let Some(cb) = self.callback.read().as_ref() {
            cb.on_event(event);
        }
    }
}

impl UIHandler for BridgeUiHandler {
    fn show_popup(&self, _items: &[ClipboardItem]) -> clipboard_core::error::Result<()> {
        // In bridge mode, we just notify the UI that it should show
        // The actual popup is managed by the native UI
        self.send_event(BridgeEvent::HotkeyTriggered);
        Ok(())
    }

    fn hide_popup(&self) -> clipboard_core::error::Result<()> {
        // Native UI handles this
        Ok(())
    }

    fn is_popup_visible(&self) -> bool {
        // Native UI manages popup state
        false
    }

    fn show_tray(&self) -> clipboard_core::error::Result<()> {
        // Native UI handles tray icon
        Ok(())
    }

    fn hide_tray(&self) -> clipboard_core::error::Result<()> {
        Ok(())
    }

    fn set_tray_tooltip(&self, _tooltip: &str) -> clipboard_core::error::Result<()> {
        // Native UI handles tray tooltip
        Ok(())
    }

    fn set_theme(&self, theme: Theme) -> clipboard_core::error::Result<()> {
        let is_dark = matches!(theme, Theme::Dark);
        self.send_event(BridgeEvent::ThemeChanged { is_dark });
        Ok(())
    }

    fn on_event(&self, _callback: Box<dyn Fn(UIEvent) + Send + Sync>) {
        // Events are handled directly by native UI
    }

    fn update_settings(
        &self,
        _settings: &clipcy_settings_common::Settings,
    ) -> clipboard_core::error::Result<()> {
        Ok(())
    }

    fn get_settings(&self) -> Settings {
        Settings::default()
    }
}

// ============================================
// Main Engine
// ============================================

/// Main clipboard engine - exposed to native UI via UniFFI
pub struct ClipboardEngine {
    runtime: Runtime,
    repository: Arc<SqliteClipboardRepository>,
    clipboard_service: Arc<RwLock<Option<Arc<ClipboardService>>>>,
    settings_manager: SettingsManager,
    ui_handler: Arc<BridgeUiHandler>,
    is_running: Arc<RwLock<bool>>,

    #[cfg(target_os = "windows")]
    monitor: Arc<WindowsClipboardMonitor>,
    #[cfg(target_os = "windows")]
    paste_handler: Arc<WindowsPasteHandler>,
    #[cfg(target_os = "windows")]
    hotkey_handler: Arc<WindowsHotkeyHandler>,
    #[cfg(target_os = "windows")]
    theme_detector: Arc<WindowsThemeDetector>,
    #[cfg(target_os = "windows")]
    autostart_handler: Arc<WindowsAutostartHandler>,
}

impl ClipboardEngine {
    /// Create a new clipboard engine
    pub fn new(db_path: String) -> Result<Self, BridgeError> {
        // Create tokio runtime for async operations
        let runtime = Runtime::new().map_err(|e| BridgeError::PlatformError(e.to_string()))?;

        // Initialize repository
        let db_path = PathBuf::from(&db_path);

        // Ensure parent directory exists
        if let Some(parent) = db_path.parent() {
            std::fs::create_dir_all(parent)
                .map_err(|e| BridgeError::StorageError(e.to_string()))?;
        }

        let repository = runtime
            .block_on(SqliteClipboardRepository::new(db_path))
            .map_err(|e| BridgeError::StorageError(e.to_string()))?;

        // Initialize settings manager
        let settings_manager =
            SettingsManager::new().map_err(|e| BridgeError::StorageError(e.to_string()))?;

        // Create platform handlers
        #[cfg(target_os = "windows")]
        {
            Ok(Self {
                runtime,
                repository: Arc::new(repository),
                clipboard_service: Arc::new(RwLock::new(None)),
                settings_manager,
                ui_handler: Arc::new(BridgeUiHandler::new()),
                is_running: Arc::new(RwLock::new(false)),
                monitor: Arc::new(WindowsClipboardMonitor::new()),
                paste_handler: Arc::new(WindowsPasteHandler::new()),
                hotkey_handler: Arc::new(WindowsHotkeyHandler::new()),
                theme_detector: Arc::new(WindowsThemeDetector::new()),
                autostart_handler: Arc::new(WindowsAutostartHandler::new()),
            })
        }

        #[cfg(not(target_os = "windows"))]
        {
            Err(BridgeError::PlatformError(
                "Only Windows is supported currently".to_string(),
            ))
        }
    }

    /// Start the clipboard engine with a UI callback
    pub fn start(&self, callback: Box<dyn UiCallback>) -> Result<(), BridgeError> {
        if *self.is_running.read() {
            return Err(BridgeError::AlreadyRunning);
        }

        // Set the callback
        self.ui_handler.set_callback(Arc::from(callback));

        // Load settings
        let settings = self
            .settings_manager
            .load_or_create()
            .map_err(|e| BridgeError::StorageError(e.to_string()))?;

        // The settings from settings_manager is already the core Settings type
        // (clipboard_core::domain::Settings is re-exported from clipcy_settings_common)
        let core_settings = settings.clone();

        // Create clipboard service
        #[cfg(target_os = "windows")]
        {
            let service = Arc::new(ClipboardService::new(
                self.monitor.clone(),
                self.repository.clone(),
                self.paste_handler.clone(),
                core_settings,
                self.runtime.handle().clone(),
            ));

            // Store service reference
            *self.clipboard_service.write() = Some(service.clone());

            // Set up clipboard change callback
            let ui_handler = self.ui_handler.clone();
            self.monitor.on_change(Box::new(move |item| {
                ui_handler.send_event(BridgeEvent::ClipboardChanged { item: item.into() });
                ui_handler.send_event(BridgeEvent::HistoryUpdated);
            }));

            // Start monitoring
            service.start_monitoring().map_err(|e| {
                BridgeError::PlatformError(format!("Failed to start monitoring: {}", e))
            })?;

            // Set up hotkey
            let ui_handler = self.ui_handler.clone();
            let _ = self.hotkey_handler.register(
                &settings.hotkey,
                Box::new(move || {
                    ui_handler.send_event(BridgeEvent::HotkeyTriggered);
                }),
            );

            // Set up theme detection
            let ui_handler = self.ui_handler.clone();
            if let Ok(theme) = self.theme_detector.get_current() {
                let is_dark = matches!(theme, Theme::Dark);
                ui_handler.send_event(BridgeEvent::ThemeChanged { is_dark });
            }

            let _ = self.theme_detector.observe(Box::new(move |theme| {
                let is_dark = matches!(theme, Theme::Dark);
                ui_handler.send_event(BridgeEvent::ThemeChanged { is_dark });
            }));
        }

        *self.is_running.write() = true;
        tracing::info!("Clipboard engine started");

        Ok(())
    }

    /// Stop the clipboard engine
    pub fn stop(&self) -> Result<(), BridgeError> {
        if !*self.is_running.read() {
            return Ok(());
        }

        #[cfg(target_os = "windows")]
        {
            if let Some(service) = self.clipboard_service.read().as_ref() {
                let _ = service.stop_monitoring();
            }
            let _ = self.hotkey_handler.unregister_all();
            let _ = self.theme_detector.stop_observing();
        }

        *self.is_running.write() = false;
        tracing::info!("Clipboard engine stopped");

        Ok(())
    }

    /// Get clipboard history (uses max_history from settings)
    pub fn get_history(&self) -> Vec<ClipboardItemDto> {
        if let Some(service) = self.clipboard_service.read().as_ref() {
            // Load settings to get max_history
            let limit = self
                .settings_manager
                .load()
                .map(|s| s.max_history_items)
                .unwrap_or(100);

            self.runtime
                .block_on(service.get_history(Some(limit)))
                .map(|items| items.into_iter().map(Into::into).collect())
                .unwrap_or_default()
        } else {
            Vec::new()
        }
    }

    /// Search clipboard history
    pub fn search(&self, query: String) -> Vec<ClipboardItemDto> {
        if let Some(service) = self.clipboard_service.read().as_ref() {
            self.runtime
                .block_on(service.search(&query))
                .map(|items| items.into_iter().map(Into::into).collect())
                .unwrap_or_default()
        } else {
            Vec::new()
        }
    }

    /// Paste an item by ID
    pub fn paste_item(&self, id: String) -> Result<(), BridgeError> {
        let uuid = Uuid::parse_str(&id).map_err(|_| BridgeError::InvalidId)?;

        if let Some(service) = self.clipboard_service.read().as_ref() {
            self.runtime
                .block_on(service.paste_item(uuid))
                .map_err(|e| BridgeError::PlatformError(e.to_string()))?;
        } else {
            return Err(BridgeError::NotInitialized);
        }

        Ok(())
    }

    /// Delete an item by ID
    pub fn delete_item(&self, id: String) -> Result<(), BridgeError> {
        let uuid = Uuid::parse_str(&id).map_err(|_| BridgeError::InvalidId)?;

        if let Some(service) = self.clipboard_service.read().as_ref() {
            self.runtime
                .block_on(service.delete_item(uuid))
                .map_err(|e| BridgeError::StorageError(e.to_string()))?;

            self.ui_handler.send_event(BridgeEvent::HistoryUpdated);
        } else {
            return Err(BridgeError::NotInitialized);
        }

        Ok(())
    }

    /// Clear all non-pinned history
    pub fn clear_history(&self) -> Result<(), BridgeError> {
        if let Some(service) = self.clipboard_service.read().as_ref() {
            self.runtime
                .block_on(service.clear_history())
                .map_err(|e| BridgeError::StorageError(e.to_string()))?;

            self.ui_handler.send_event(BridgeEvent::HistoryUpdated);
        } else {
            return Err(BridgeError::NotInitialized);
        }

        Ok(())
    }

    /// Set pinned status for an item
    pub fn set_pinned(&self, id: String, pinned: bool) -> Result<(), BridgeError> {
        let uuid = Uuid::parse_str(&id).map_err(|_| BridgeError::InvalidId)?;

        if let Some(service) = self.clipboard_service.read().as_ref() {
            self.runtime
                .block_on(service.set_pinned(uuid, pinned))
                .map_err(|e| BridgeError::StorageError(e.to_string()))?;

            self.ui_handler.send_event(BridgeEvent::HistoryUpdated);
        } else {
            return Err(BridgeError::NotInitialized);
        }

        Ok(())
    }

    /// Get full item data (for preview/paste)
    pub fn get_item_data(&self, id: String) -> Option<Vec<u8>> {
        let uuid = Uuid::parse_str(&id).ok()?;

        self.runtime
            .block_on(self.repository.get_by_id(uuid))
            .ok()
            .flatten()
            .map(|item| item.data)
    }

    /// Get current settings
    pub fn get_settings(&self) -> SettingsDto {
        self.settings_manager
            .load()
            .map(Into::into)
            .unwrap_or_else(|_| clipcy_settings_common::Settings::default().into())
    }

    /// Save settings and apply them immediately
    pub fn save_settings(&self, settings: SettingsDto) -> Result<(), BridgeError> {
        let settings: clipcy_settings_common::Settings = settings.into();
        self.settings_manager
            .save(&settings)
            .map_err(|e| BridgeError::StorageError(e.to_string()))?;

        // Apply settings immediately
        #[cfg(target_os = "windows")]
        {
            // Update autostart if needed
            let current_enabled = self.autostart_handler.is_enabled().unwrap_or(false);
            if settings.start_on_boot != current_enabled {
                if settings.start_on_boot {
                    let _ = self.autostart_handler.enable();
                } else {
                    let _ = self.autostart_handler.disable();
                }
            }

            // Re-register hotkey with new settings
            if let Some(_service) = self.clipboard_service.read().as_ref() {
                let _ = self.hotkey_handler.unregister_all();
                let ui_handler = self.ui_handler.clone();
                let _ = self.hotkey_handler.register(
                    &settings.hotkey,
                    Box::new(move || {
                        ui_handler.send_event(BridgeEvent::HotkeyTriggered);
                    }),
                );
            }

            // Apply theme change immediately
            let theme = match settings.theme {
                clipcy_settings_common::ThemeMode::Light => Theme::Light,
                clipcy_settings_common::ThemeMode::Dark => Theme::Dark,
                clipcy_settings_common::ThemeMode::System => {
                    self.theme_detector.get_current().unwrap_or(Theme::Light)
                }
            };
            let is_dark = matches!(theme, Theme::Dark);
            self.ui_handler.send_event(BridgeEvent::ThemeChanged { is_dark });
        }

        Ok(())
    }

    /// Check if current theme is dark
    pub fn is_dark_theme(&self) -> bool {
        #[cfg(target_os = "windows")]
        {
            self.theme_detector
                .get_current()
                .map(|t| matches!(t, Theme::Dark))
                .unwrap_or(false)
        }

        #[cfg(not(target_os = "windows"))]
        {
            false
        }
    }
}
