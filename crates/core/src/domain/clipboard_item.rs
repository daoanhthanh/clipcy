use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use super::content_type::ContentType;

/// Simple HTML tag stripper for preview generation
/// Removes HTML tags and decodes common HTML entities to extract plain text
pub(crate) fn strip_html_tags(html: &str) -> String {
    let mut result = String::with_capacity(html.len());
    let mut in_tag = false;

    for ch in html.chars() {
        match ch {
            '<' => in_tag = true,
            '>' => in_tag = false,
            _ if !in_tag => result.push(ch),
            _ => {}
        }
    }

    // Decode common HTML entities
    let result = decode_html_entities(&result);

    // Collapse whitespace and trim
    result.split_whitespace().collect::<Vec<_>>().join(" ")
}

/// Decode common HTML entities to their character equivalents
pub(crate) fn decode_html_entities(s: &str) -> String {
    s.replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&amp;", "&")
        .replace("&quot;", "\"")
        .replace("&apos;", "'")
        .replace("&#39;", "'")
        .replace("&nbsp;", " ")
}

/// Represents a single clipboard history item
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ClipboardItem {
    /// Unique identifier
    pub id: Uuid,
    /// Type of content
    pub content_type: ContentType,
    /// Raw content data
    pub data: Vec<u8>,
    /// Preview text (first 100 chars for text, filename for files)
    pub preview: String,
    /// Thumbnail for images (small PNG, max 64x64)
    pub thumbnail: Option<Vec<u8>>,
    /// Source application name (if available)
    pub source_app: Option<String>,
    /// Timestamp when copied
    pub created_at: DateTime<Utc>,
    /// Whether item is pinned (won't be auto-deleted)
    pub pinned: bool,
}

impl ClipboardItem {
    /// Create new text clipboard item
    pub fn new_text(text: impl Into<String>) -> Self {
        let text = text.into();
        let preview = text.chars().take(100).collect();

        Self {
            id: Uuid::new_v4(),
            content_type: ContentType::Text,
            data: text.into_bytes(),
            preview,
            thumbnail: None,
            source_app: None,
            created_at: Utc::now(),
            pinned: false,
        }
    }

    /// Create new image clipboard item
    pub fn new_image(png_data: Vec<u8>, thumbnail: Option<Vec<u8>>) -> Self {
        Self {
            id: Uuid::new_v4(),
            content_type: ContentType::Image,
            data: png_data,
            preview: "[Image]".to_string(),
            thumbnail,
            source_app: None,
            created_at: Utc::now(),
            pinned: false,
        }
    }

    /// Create new files clipboard item
    pub fn new_files(file_paths: Vec<String>) -> Self {
        let preview = if file_paths.len() == 1 {
            file_paths[0].clone()
        } else {
            format!("{} files", file_paths.len())
        };

        let data = file_paths.join("\n").into_bytes();

        Self {
            id: Uuid::new_v4(),
            content_type: ContentType::Files,
            data,
            preview,
            thumbnail: None,
            source_app: None,
            created_at: Utc::now(),
            pinned: false,
        }
    }

    /// Create new HTML clipboard item
    pub fn new_html(html: impl Into<String>) -> Self {
        let html = html.into();
        // Extract text preview from HTML (strip tags for preview)
        let preview = strip_html_tags(&html).chars().take(100).collect();

        Self {
            id: Uuid::new_v4(),
            content_type: ContentType::Html,
            data: html.into_bytes(),
            preview,
            thumbnail: None,
            source_app: None,
            created_at: Utc::now(),
            pinned: false,
        }
    }

    /// Create new Rich Text (RTF) clipboard item
    pub fn new_rtf(rtf: impl Into<String>) -> Self {
        let rtf = rtf.into();
        // RTF preview: show beginning of content (RTF is not human-readable, so limited preview)
        let preview = if rtf.len() > 100 {
            format!("[RTF] {} chars", rtf.len())
        } else {
            "[RTF Content]".to_string()
        };

        Self {
            id: Uuid::new_v4(),
            content_type: ContentType::RichText,
            data: rtf.into_bytes(),
            preview,
            thumbnail: None,
            source_app: None,
            created_at: Utc::now(),
            pinned: false,
        }
    }

    /// Get text content if this is a text item
    pub fn as_text(&self) -> Option<&str> {
        if self.content_type == ContentType::Text {
            std::str::from_utf8(&self.data).ok()
        } else {
            None
        }
    }
}

impl Default for ClipboardItem {
    fn default() -> Self {
        Self::new_text("")
    }
}

#[cfg(test)]
#[path = "clipboard_item_tests.rs"]
mod tests;
