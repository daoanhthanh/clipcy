use serde::{Deserialize, Serialize};
use std::fs;
use std::io::Write;
use std::path::PathBuf;
use thiserror::Error;

#[derive(Error, Debug)]
pub enum SettingsError {
    #[error("IO error: {0}")]
    Io(#[from] std::io::Error),
    #[error("JSON error: {0}")]
    Json(#[from] serde_json::Error),
    #[error("Could not determine settings directory")]
    NoSettingsDir,
}

pub type Result<T> = std::result::Result<T, SettingsError>;

/// Theme mode for the application
#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Default)]
#[serde(rename_all = "lowercase")]
pub enum ThemeMode {
    Light,
    Dark,
    #[default]
    System,
}

/// Application settings
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Settings {
    /// Maximum number of items to keep in history
    #[serde(default = "default_max_history")]
    pub max_history_items: usize,
    /// Hotkey combination string (e.g., "Ctrl+Shift+V")
    #[serde(default = "default_hotkey")]
    pub hotkey: String,
    /// Theme mode (light, dark, or system)
    #[serde(default)]
    pub theme: ThemeMode,
    /// Whether to start on system boot
    #[serde(default = "default_true")]
    pub start_on_boot: bool,
    /// Whether to show notifications
    #[serde(default)]
    pub show_notifications: bool,
    /// Whether to show preview panel in popup
    #[serde(default = "default_true")]
    pub show_preview_panel: bool,
    /// Excluded applications (won't capture from these)
    #[serde(default)]
    pub excluded_apps: Vec<String>,
}

fn default_max_history() -> usize {
    100
}

fn default_hotkey() -> String {
    "Ctrl+Shift+V".to_string()
}

fn default_true() -> bool {
    true
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            max_history_items: 100,
            hotkey: "Ctrl+Shift+V".to_string(),
            theme: ThemeMode::System,
            start_on_boot: true,
            show_notifications: false,
            show_preview_panel: true,
            excluded_apps: Vec::new(),
        }
    }
}

/// Manages settings persistence
#[derive(Debug)]
pub struct SettingsManager {
    path: PathBuf,
}

impl SettingsManager {
    /// Create a new settings manager with the default path
    pub fn new() -> Result<Self> {
        let path = Self::default_path()?;
        Ok(Self { path })
    }

    /// Create a settings manager with a custom path
    pub fn with_path(path: PathBuf) -> Self {
        Self { path }
    }

    /// Get the default settings file path
    pub fn default_path() -> Result<PathBuf> {
        let dir = dirs::data_local_dir().ok_or(SettingsError::NoSettingsDir)?;
        Ok(dir.join("clipcy").join("settings.json"))
    }

    /// Get the settings file path
    pub fn path(&self) -> &PathBuf {
        &self.path
    }

    /// Load settings from file, returning defaults if file doesn't exist
    pub fn load(&self) -> Result<Settings> {
        if !self.path.exists() {
            tracing::info!("Settings file not found, using defaults");
            return Ok(Settings::default());
        }

        let content = fs::read_to_string(&self.path)?;
        let settings: Settings = serde_json::from_str(&content)?;
        tracing::debug!("Loaded settings from {:?}", self.path);
        Ok(settings)
    }

    /// Save settings to file (atomic write)
    pub fn save(&self, settings: &Settings) -> Result<()> {
        // Ensure parent directory exists
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent)?;
        }

        // Write to temp file first for atomic operation
        let temp_path = self.path.with_extension("json.tmp");
        let content = serde_json::to_string_pretty(settings)?;

        {
            let mut file = fs::File::create(&temp_path)?;
            file.write_all(content.as_bytes())?;
            file.sync_all()?;
        }

        // Atomic rename
        fs::rename(&temp_path, &self.path)?;
        tracing::debug!("Saved settings to {:?}", self.path);
        Ok(())
    }

    /// Load settings or create default file if it doesn't exist
    pub fn load_or_create(&self) -> Result<Settings> {
        if !self.path.exists() {
            let settings = Settings::default();
            self.save(&settings)?;
            return Ok(settings);
        }
        self.load()
    }
}

impl Default for SettingsManager {
    fn default() -> Self {
        Self::new().expect("Failed to create settings manager")
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[test]
    fn test_settings_default() {
        let settings = Settings::default();
        assert_eq!(settings.max_history_items, 100);
        assert_eq!(settings.hotkey, "Ctrl+Shift+V");
        assert!(settings.start_on_boot);
        assert!(settings.show_preview_panel);
    }

    #[test]
    fn test_settings_save_load() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("settings.json");
        let manager = SettingsManager::with_path(path);

        let mut settings = Settings::default();
        settings.max_history_items = 50;
        settings.hotkey = "Ctrl+Alt+V".to_string();

        manager.save(&settings).unwrap();
        let loaded = manager.load().unwrap();

        assert_eq!(loaded.max_history_items, 50);
        assert_eq!(loaded.hotkey, "Ctrl+Alt+V");
    }

    #[test]
    fn test_settings_load_missing_file() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("nonexistent.json");
        let manager = SettingsManager::with_path(path);

        let settings = manager.load().unwrap();
        assert_eq!(settings, Settings::default());
    }
}
