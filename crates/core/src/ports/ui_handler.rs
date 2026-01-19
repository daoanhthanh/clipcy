use crate::domain::{ClipboardItem, Settings, Theme};
use crate::error::Result;

/// Menu item selection callback
pub type SelectCallback = Box<dyn Fn(usize) + Send + Sync>;

/// UI event types
pub enum UIEvent {
    ItemSelected(usize),
    SettingsClicked,
    ClearHistoryClicked,
    QuitClicked,
    SearchChanged(String),
    AutostartToggled(bool),
    PreviewPanelToggled(bool),
}

/// Callback for UI events
pub type UIEventCallback = Box<dyn Fn(UIEvent) + Send + Sync>;

/// Platform-specific native UI
pub trait UIHandler: Send + Sync {
    /// Show popup menu at current cursor position
    fn show_popup(&self, items: &[ClipboardItem]) -> Result<()>;

    /// Hide popup menu
    fn hide_popup(&self) -> Result<()>;

    /// Check if popup is visible
    fn is_popup_visible(&self) -> bool;

    /// Update theme
    fn set_theme(&self, theme: Theme) -> Result<()>;

    /// Set callback for UI events
    fn on_event(&self, callback: UIEventCallback);

    /// Show system tray icon
    fn show_tray(&self) -> Result<()>;

    /// Hide system tray icon
    fn hide_tray(&self) -> Result<()>;

    /// Update tray tooltip
    fn set_tray_tooltip(&self, tooltip: &str) -> Result<()>;

    /// Update settings for tray menu display
    fn update_settings(&self, settings: &Settings) -> Result<()>;

    /// Get current settings from UI handler
    fn get_settings(&self) -> Settings;
}
