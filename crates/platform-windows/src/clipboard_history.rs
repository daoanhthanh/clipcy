use clipboard_core::domain::ClipboardItem;
use clipboard_core::error::Result;
use std::collections::VecDeque;
use tracing::warn;

/// Import clipboard history items from Windows
///
/// Currently returns an empty list as a fallback since the Windows clipboard
/// history API (GetHistoryItemsAsync) requires additional WinRT features
/// not enabled in the current windows crate configuration.
///
/// This wraps with try/catch and returns empty list on error, as requested.
pub async fn import_system_clipboard_history() -> Result<VecDeque<ClipboardItem>> {
    match import_history_internal().await {
        Ok(items) => {
            if !items.is_empty() {
                tracing::info!("Successfully imported {} clipboard history items", items.len());
            } else {
                tracing::debug!("No clipboard history items imported");
            }
            Ok(items)
        }
        Err(e) => {
            warn!("Failed to import system clipboard history: {}", e);
            // Fallback to empty list as requested
            Ok(VecDeque::new())
        }
    }
}

async fn import_history_internal() -> Result<VecDeque<ClipboardItem>> {
    // Windows clipboard history API (GetHistoryItemsAsync) requires:
    // - Windows::Data::Clipboard feature
    // - Windows::Storage feature
    // - Windows::Graphics::Imaging feature
    //
    // These are not currently enabled in the windows crate dependency.
    // To enable them, add to crates/platform-windows/Cargo.toml:
    // - "Data_Clipboard"
    // - "Storage"
    // - "Graphics_Imaging"
    //
    // For now, we return an empty list (safe fallback).
    // The feature can be implemented later when dependencies are updated.

    Ok(VecDeque::new())
}

