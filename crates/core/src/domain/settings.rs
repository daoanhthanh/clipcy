use serde::{Deserialize, Serialize};

/// Application settings
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Settings {
    /// Maximum number of items to keep in history
    pub max_history_items: usize,
    /// Hotkey combination string (e.g., "Ctrl+Shift+V")
    pub hotkey: String,
    /// Whether to sync theme with system
    pub sync_theme: bool,
    /// Whether to start on system boot
    pub start_on_boot: bool,
    /// Whether to show notifications
    pub show_notifications: bool,
    /// Excluded applications (won't capture from these)
    pub excluded_apps: Vec<String>,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            max_history_items: 100,
            hotkey: "Ctrl+Shift+V".to_string(),
            sync_theme: true,
            start_on_boot: true,
            show_notifications: false,
            excluded_apps: Vec::new(),
        }
    }
}
