use parking_lot::RwLock;
use serde::{Deserialize, Serialize};
use std::fs;
use std::io::Write;
use std::path::PathBuf;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
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
    /// Hotkey combination string (e.g., "Alt+V")
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
    /// Whether this is the first run (used for initial clipboard import)
    #[serde(default = "default_true")]
    pub first_run: bool,
}

pub const MIN_MAX_HISTORY_ITEMS: usize = 25;

fn normalize_settings(mut settings: Settings) -> Settings {
    settings.max_history_items = settings.max_history_items.max(MIN_MAX_HISTORY_ITEMS);
    settings
}

fn default_max_history() -> usize {
    210
}

fn default_hotkey() -> String {
    "Alt+V".to_string()
}

fn default_true() -> bool {
    true
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            max_history_items: default_max_history(),
            hotkey: default_hotkey(),
            theme: ThemeMode::System,
            start_on_boot: default_true(),
            show_notifications: false,
            show_preview_panel: default_true(),
            excluded_apps: Vec::new(),
            first_run: default_true(),
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
        Ok(normalize_settings(settings))
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
        assert_eq!(settings.max_history_items, 210);
        assert_eq!(settings.hotkey, "Alt+V");
        assert!(settings.start_on_boot);
        assert!(settings.show_preview_panel);
    }

    #[test]
    fn test_settings_minimum_history_items() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("settings.json");
        let manager = SettingsManager::with_path(path.clone());
        fs::write(&path, r#"{"max_history_items":10}"#).unwrap();

        assert_eq!(manager.load().unwrap().max_history_items, MIN_MAX_HISTORY_ITEMS);

        let cached = CachedSettingsManager::with_path(path);
        assert_eq!(cached.load_now().unwrap().max_history_items, MIN_MAX_HISTORY_ITEMS);

        let mut settings = Settings::default();
        settings.max_history_items = 10;
        cached.save(&settings).unwrap();
        assert_eq!(cached.get().max_history_items, MIN_MAX_HISTORY_ITEMS);
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

/// Cached settings manager for fast startup
///
/// Returns default settings immediately and loads from disk in the background.
/// This eliminates blocking I/O during application startup.
#[derive(Debug, Clone)]
pub struct CachedSettingsManager {
    cache: Arc<RwLock<Settings>>,
    path: PathBuf,
    loaded: Arc<AtomicBool>,
}

impl CachedSettingsManager {
    /// Create a new cached settings manager with default path
    pub fn new() -> Result<Self> {
        let path = SettingsManager::default_path()?;
        Ok(Self::with_path(path))
    }

    /// Create a cached settings manager with custom path
    pub fn with_path(path: PathBuf) -> Self {
        Self {
            cache: Arc::new(RwLock::new(Settings::default())),
            path,
            loaded: Arc::new(AtomicBool::new(false)),
        }
    }

    /// Get the settings file path
    pub fn path(&self) -> &PathBuf {
        &self.path
    }

    /// Get current settings (returns immediately, may be defaults or loaded)
    pub fn get(&self) -> Settings {
        self.cache.read().clone()
    }

    /// Check if settings have been loaded from disk
    pub fn is_loaded(&self) -> bool {
        self.loaded.load(Ordering::Acquire)
    }

    /// Load settings from disk synchronously (for immediate use)
    pub fn load_now(&self) -> Result<Settings> {
        let settings = if !self.path.exists() {
            tracing::info!("Settings file not found, using defaults");
            Settings::default()
        } else {
            let content = fs::read_to_string(&self.path)?;
            let settings: Settings = serde_json::from_str(&content)?;
            tracing::debug!("Loaded settings from {:?}", self.path);
            settings
        };
        let settings = normalize_settings(settings);

        *self.cache.write() = settings.clone();
        self.loaded.store(true, Ordering::Release);
        Ok(settings)
    }

    /// Start loading settings from disk asynchronously
    ///
    /// This spawns a background task that loads settings without blocking.
    /// Call get() to retrieve settings (will be defaults until load completes).
    pub fn load_async<F>(&self, on_loaded: F)
    where
        F: FnOnce(Result<Settings>) + Send + 'static,
    {
        let cache = self.cache.clone();
        let path = self.path.clone();
        let loaded = self.loaded.clone();

        std::thread::spawn(move || {
            let result = if !path.exists() {
                tracing::info!("Settings file not found (async load), using defaults");
                Ok(Settings::default())
            } else {
                match fs::read_to_string(&path) {
                    Ok(content) => match serde_json::from_str(&content) {
                        Ok(settings) => {
                            tracing::debug!("Loaded settings asynchronously from {:?}", path);
                            Ok(normalize_settings(settings))
                        }
                        Err(e) => Err(SettingsError::Json(e)),
                    },
                    Err(e) => Err(SettingsError::Io(e)),
                }
            };

            if let Ok(ref settings) = result {
                *cache.write() = settings.clone();
                loaded.store(true, Ordering::Release);
            }

            on_loaded(result);
        });
    }

    /// Save settings to file (atomic write)
    pub fn save(&self, settings: &Settings) -> Result<()> {
        let settings = normalize_settings(settings.clone());
        // Update cache first
        *self.cache.write() = settings.clone();

        // Ensure parent directory exists
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent)?;
        }

        // Write to temp file first for atomic operation
        let temp_path = self.path.with_extension("json.tmp");
        let content = serde_json::to_string_pretty(&settings)?;

        {
            let mut file = fs::File::create(&temp_path)?;
            file.write_all(content.as_bytes())?;
            file.sync_all()?;
        }

        // Atomic rename
        fs::rename(&temp_path, &self.path)?;
        self.loaded.store(true, Ordering::Release);
        tracing::debug!("Saved settings to {:?}", self.path);
        Ok(())
    }

    /// Load settings or create default file if it doesn't exist (synchronous)
    pub fn load_or_create(&self) -> Result<Settings> {
        if !self.path.exists() {
            let settings = Settings::default();
            self.save(&settings)?;
            return Ok(settings);
        }
        self.load_now()
    }
}

impl Default for CachedSettingsManager {
    fn default() -> Self {
        Self::new().expect("Failed to create cached settings manager")
    }
}
