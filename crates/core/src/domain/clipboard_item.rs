use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use super::content_type::ContentType;

/// Simple HTML tag stripper for preview generation
/// Removes HTML tags to extract plain text for preview
fn strip_html_tags(html: &str) -> String {
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

    // Collapse whitespace and trim
    result.split_whitespace().collect::<Vec<_>>().join(" ")
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
mod tests {
    use super::*;

    #[test]
    fn test_new_text() {
        let item = ClipboardItem::new_text("Hello World");
        assert_eq!(item.content_type, ContentType::Text);
        assert_eq!(item.as_text(), Some("Hello World"));
        assert_eq!(item.preview, "Hello World");
    }

    #[test]
    fn test_preview_truncation() {
        let long_text = "a".repeat(200);
        let item = ClipboardItem::new_text(&long_text);
        assert_eq!(item.preview.len(), 100);
    }

    #[test]
    fn test_new_files() {
        let files = vec!["/path/to/file1.txt".to_string(), "/path/to/file2.txt".to_string()];
        let item = ClipboardItem::new_files(files);
        assert_eq!(item.content_type, ContentType::Files);
        assert_eq!(item.preview, "2 files");
    }

    #[test]
    fn test_new_image() {
        let png_data = vec![0x89, 0x50, 0x4E, 0x47]; // PNG magic bytes
        let item = ClipboardItem::new_image(png_data.clone(), None);
        assert_eq!(item.content_type, ContentType::Image);
        assert_eq!(item.preview, "[Image]");
        assert_eq!(item.data, png_data);
    }

    #[test]
    fn test_single_file() {
        let files = vec!["/path/to/file.txt".to_string()];
        let item = ClipboardItem::new_files(files);
        assert_eq!(item.preview, "/path/to/file.txt");
    }

    #[test]
    fn test_new_html() {
        let html = "<html><body><p>Hello World</p></body></html>";
        let item = ClipboardItem::new_html(html);
        assert_eq!(item.content_type, ContentType::Html);
        assert_eq!(item.preview, "Hello World");
        assert_eq!(String::from_utf8_lossy(&item.data), html);
    }

    #[test]
    fn test_new_html_complex() {
        let html = "<div class='test'><span>Some</span> <b>formatted</b> text</div>";
        let item = ClipboardItem::new_html(html);
        assert_eq!(item.content_type, ContentType::Html);
        assert_eq!(item.preview, "Some formatted text");
    }

    #[test]
    fn test_new_rtf() {
        let rtf = r"{\rtf1\ansi\deff0 Hello World}";
        let item = ClipboardItem::new_rtf(rtf);
        assert_eq!(item.content_type, ContentType::RichText);
        assert_eq!(item.preview, "[RTF Content]");
        assert_eq!(String::from_utf8_lossy(&item.data), rtf);
    }

    #[test]
    fn test_new_rtf_long() {
        let rtf = r"{\rtf1\ansi\deff0 ".to_string() + &"a".repeat(200) + "}";
        let item = ClipboardItem::new_rtf(&rtf);
        assert_eq!(item.content_type, ContentType::RichText);
        assert!(item.preview.starts_with("[RTF]"));
        assert!(item.preview.contains("chars"));
    }

    #[test]
    fn test_strip_html_tags() {
        assert_eq!(super::strip_html_tags("<p>Hello</p>"), "Hello");
        assert_eq!(super::strip_html_tags("<div><span>A</span> <span>B</span></div>"), "A B");
        assert_eq!(super::strip_html_tags("No tags here"), "No tags here");
        assert_eq!(super::strip_html_tags("<script>alert('xss')</script>"), "alert('xss')");
    }
}
