use serde::{Deserialize, Serialize};

/// Represents the type of content stored in clipboard
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum ContentType {
    /// Plain text content
    #[default]
    Text,
    /// Rich text (RTF format)
    RichText,
    /// HTML content
    Html,
    /// Image data (PNG format stored)
    Image,
    /// File reference(s)
    Files,
}

impl ContentType {
    /// Returns MIME type string for the content type
    pub fn mime_type(&self) -> &'static str {
        match self {
            ContentType::Text => "text/plain",
            ContentType::RichText => "text/rtf",
            ContentType::Html => "text/html",
            ContentType::Image => "image/png",
            ContentType::Files => "text/uri-list",
        }
    }
}
