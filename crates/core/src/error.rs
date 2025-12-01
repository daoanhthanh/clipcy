use thiserror::Error;

#[derive(Error, Debug)]
pub enum ClipboardError {
    #[error("Clipboard access error: {0}")]
    ClipboardAccess(String),

    #[error("Storage error: {0}")]
    Storage(String),

    #[error("Hotkey registration failed: {0}")]
    HotkeyRegistration(String),

    #[error("Paste simulation failed: {0}")]
    PasteSimulation(String),

    #[error("Theme detection failed: {0}")]
    ThemeDetection(String),

    #[error("UI error: {0}")]
    UI(String),

    #[error("Serialization error: {0}")]
    Serialization(#[from] serde_json::Error),

    #[error("IO error: {0}")]
    Io(#[from] std::io::Error),

    #[error("Platform not supported: {0}")]
    PlatformNotSupported(String),
}

pub type Result<T> = std::result::Result<T, ClipboardError>;
