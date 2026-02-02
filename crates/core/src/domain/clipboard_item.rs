use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use super::content_type::ContentType;

/// Simple HTML tag stripper for preview generation
/// Removes HTML tags and decodes common HTML entities to extract plain text
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

    // Decode common HTML entities
    let result = decode_html_entities(&result);

    // Collapse whitespace and trim
    result.split_whitespace().collect::<Vec<_>>().join(" ")
}

fn looks_like_table_html(html: &str) -> bool {
    let lower = html.to_ascii_lowercase();
    lower.contains("<table") || (lower.contains("<tr") && lower.contains("<td"))
}

fn extract_table_rows(html: &str) -> Vec<Vec<String>> {
    let lower = html.to_ascii_lowercase();
    let mut rows = Vec::new();
    let mut idx = 0;

    loop {
        let row_pos = match lower[idx..].find("<tr") {
            Some(pos) => pos + idx,
            None => break,
        };

        let row_open_end = match lower[row_pos..].find('>') {
            Some(pos) => row_pos + pos + 1,
            None => break,
        };

        let row_close = match lower[row_open_end..].find("</tr") {
            Some(pos) => row_open_end + pos,
            None => break,
        };

        let row_html = &html[row_open_end..row_close];
        let row_lower = row_html.to_ascii_lowercase();
        let mut cells = Vec::new();
        let mut cell_idx = 0;

        loop {
            let td_pos = row_lower[cell_idx..].find("<td").map(|p| p + cell_idx);
            let th_pos = row_lower[cell_idx..].find("<th").map(|p| p + cell_idx);
            let cell_start = match (td_pos, th_pos) {
                (Some(td), Some(th)) => td.min(th),
                (Some(td), None) => td,
                (None, Some(th)) => th,
                (None, None) => break,
            };

            let open_end = match row_lower[cell_start..].find('>') {
                Some(pos) => cell_start + pos + 1,
                None => break,
            };

            let closing_tag = if row_lower[cell_start..].starts_with("<th") {
                "</th"
            } else {
                "</td"
            };

            let close_start = match row_lower[open_end..].find(closing_tag) {
                Some(pos) => open_end + pos,
                None => break,
            };

            let cell_html = &row_html[open_end..close_start];
            let cell_text = strip_html_tags(cell_html).trim().to_string();
            cells.push(cell_text);

            cell_idx = close_start + closing_tag.len();
        }

        if !cells.is_empty() {
            rows.push(cells);
        }

        idx = row_close + 5;
    }

    rows
}

/// Convert HTML content to plain text
pub fn html_to_text(html: &str) -> String {
    strip_html_tags(html)
}

fn parse_html_format_offset(html_format: &str, key: &str) -> Option<usize> {
    html_format
        .split(key)
        .nth(1)
        .and_then(|s| s.lines().next())
        .and_then(|s| s.trim().parse::<usize>().ok())
}

fn extract_html_fragment_from_format(html_format: &str) -> Option<String> {
    let start_frag = parse_html_format_offset(html_format, "StartFragment:");
    let end_frag = parse_html_format_offset(html_format, "EndFragment:");

    if let (Some(start), Some(end)) = (start_frag, end_frag) {
        if start < end {
            if let Some(slice) = html_format.as_bytes().get(start..end) {
                return Some(String::from_utf8_lossy(slice).to_string());
            }
        }
    }

    let start_html = parse_html_format_offset(html_format, "StartHTML:");
    let end_html = parse_html_format_offset(html_format, "EndHTML:");

    if let (Some(start), Some(end)) = (start_html, end_html) {
        if start < end {
            if let Some(slice) = html_format.as_bytes().get(start..end) {
                return Some(String::from_utf8_lossy(slice).to_string());
            }
        }
    }

    None
}

/// Decode common HTML entities to their character equivalents
fn decode_html_entities(s: &str) -> String {
    s.replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&amp;", "&")
        .replace("&quot;", "\"")
        .replace("&apos;", "'")
        .replace("&#39;", "'")
        .replace("&nbsp;", " ")
}

/// Extract plain text from RTF content
/// Strips RTF control sequences and formatting to get readable text
fn extract_rtf_text(rtf: &str) -> String {
    let mut result = String::with_capacity(rtf.len());
    let mut brace_depth: i32 = 0;
    let mut skip_group = false;
    let mut skip_depth = 0;

    let chars: Vec<char> = rtf.chars().collect();
    let mut i = 0;

    while i < chars.len() {
        let ch = chars[i];

        match ch {
            '{' => {
                brace_depth += 1;

                // Check if this is a control group we should skip (fonttbl, colortbl, stylesheet, etc.)
                if i + 1 < chars.len() && chars[i + 1] == '\\' {
                    // Peek ahead to see if this is a control group to skip
                    let mut j = i + 2;
                    let mut control = String::new();
                    while j < chars.len() && chars[j].is_ascii_alphabetic() {
                        control.push(chars[j]);
                        j += 1;
                    }

                    // Skip these control groups
                    if matches!(
                        control.as_str(),
                        "fonttbl"
                            | "colortbl"
                            | "stylesheet"
                            | "listtable"
                            | "listoverridetable"
                            | "rsidtbl"
                            | "generator"
                            | "info"
                            | "themedata"
                            | "colorschememapping"
                            | "datastore"
                            | "xmlnstbl"
                            | "latentstyles"
                            | "lsdexception"
                    ) {
                        skip_group = true;
                        skip_depth = brace_depth;
                    }
                }

                i += 1;
            }
            '}' => {
                brace_depth = brace_depth.saturating_sub(1);

                // If we're exiting a skipped group, stop skipping
                if skip_group && brace_depth < skip_depth {
                    skip_group = false;
                }

                i += 1;
            }
            '\\' if !skip_group => {
                // Check for escaped characters
                if i + 1 < chars.len() {
                    let next = chars[i + 1];
                    if next == '\\' || next == '{' || next == '}' {
                        // Escaped special character
                        result.push(next);
                        i += 2;
                        continue;
                    }
                }

                i += 1;

                // Parse control word
                let mut control_word = String::new();

                // Collect control word letters
                while i < chars.len() && chars[i].is_ascii_alphabetic() {
                    control_word.push(chars[i]);
                    i += 1;
                }

                // Check for numeric parameter
                if i < chars.len() && (chars[i].is_ascii_digit() || chars[i] == '-') {
                    // Skip the parameter
                    if chars[i] == '-' {
                        i += 1;
                    }
                    while i < chars.len() && chars[i].is_ascii_digit() {
                        i += 1;
                    }
                }

                // Handle special control words
                match control_word.as_str() {
                    "par" | "line" => result.push('\n'),
                    "tab" => result.push('\t'),
                    "'" => {
                        // Hex-encoded character \'XX
                        if i + 1 < chars.len() {
                            let hex_str: String =
                                chars[i..std::cmp::min(i + 2, chars.len())].iter().collect();
                            if let Ok(byte_val) = u8::from_str_radix(&hex_str, 16) {
                                // Try to convert as Windows-1252 character
                                result.push(byte_val as char);
                            }
                            i += 2;
                        }
                    }
                    _ => {
                        // Ignore other control words
                    }
                }
            }
            _ if !skip_group => {
                // Regular text character (only if not in a skipped group)
                result.push(ch);
                i += 1;
            }
            _ => {
                // Skip character if in a skipped group
                i += 1;
            }
        }
    }

    // Clean up the result - collapse whitespace to single spaces
    let text = result.split_whitespace().collect::<Vec<_>>().join(" ");

    text.trim().to_string()
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

        // For preview: show only the first non-empty line (trimmed), max 100 chars
        let preview = text
            .lines()
            .map(|line| line.trim())
            .find(|line| !line.is_empty())
            .unwrap_or("")
            .chars()
            .take(100)
            .collect();

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
        let fragment = extract_html_fragment_from_format(&html).unwrap_or_else(|| html.clone());
        let preview = if looks_like_table_html(&fragment) {
            let rows = extract_table_rows(&fragment);
            if rows.is_empty() {
                "Table".to_string()
            } else {
                let table_text = rows
                    .into_iter()
                    .map(|row| row.join("\t"))
                    .collect::<Vec<_>>()
                    .join("\n");
                table_text.chars().take(8000).collect()
            }
        } else {
            // Extract text preview from HTML (strip tags for preview)
            let plain_text = strip_html_tags(&fragment);

            // For preview: show only the first non-empty line (trimmed), max 100 chars
            plain_text
                .lines()
                .map(|line| line.trim())
                .find(|line| !line.is_empty())
                .unwrap_or("")
                .chars()
                .take(100)
                .collect()
        };

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
        // Extract plain text from RTF for preview
        let plain_text = extract_rtf_text(&rtf);

        // For preview in menu: show only the first non-empty line (trimmed), max 100 chars
        let preview = if !plain_text.is_empty() {
            // Find the first non-empty line
            plain_text
                .lines()
                .map(|line| line.trim())
                .find(|line| !line.is_empty())
                .unwrap_or("")
                .chars()
                .take(100)
                .collect()
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
    fn test_new_text_with_leading_whitespace() {
        let item = ClipboardItem::new_text("\n\n  Hello World\n  Second Line");
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
        let files = vec![
            "/path/to/file1.txt".to_string(),
            "/path/to/file2.txt".to_string(),
        ];
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
    fn test_new_html_table() {
        let html = "<table><tr><td>Qty</td><td>Price</td></tr></table>";
        let item = ClipboardItem::new_html(html);
        assert_eq!(item.content_type, ContentType::Html);
        assert_eq!(item.preview, "Qty\tPrice");
    }

    #[test]
    fn test_new_rtf() {
        let rtf = r"{\rtf1\ansi\deff0 Hello World}";
        let item = ClipboardItem::new_rtf(rtf);
        assert_eq!(item.content_type, ContentType::RichText);
        assert_eq!(item.preview, "Hello World");
        assert_eq!(String::from_utf8_lossy(&item.data), rtf);
    }

    #[test]
    fn test_new_rtf_long() {
        let rtf = r"{\rtf1\ansi\deff0 ".to_string() + &"a".repeat(200) + "}";
        let item = ClipboardItem::new_rtf(&rtf);
        assert_eq!(item.content_type, ContentType::RichText);
        assert_eq!(item.preview.len(), 100);
    }

    #[test]
    fn test_extract_rtf_text() {
        assert_eq!(
            super::extract_rtf_text(r"{\rtf1 Hello World}"),
            "Hello World"
        );
        assert_eq!(
            super::extract_rtf_text(r"{\rtf1\b Bold\b0 Normal}"),
            "Bold Normal"
        );
        assert_eq!(
            super::extract_rtf_text(r"{\rtf1 Line1\par Line2}"),
            "Line1 Line2"
        );
        assert_eq!(
            super::extract_rtf_text(r"{\rtf1 Test\\Escaped\{Braces\}}"),
            "Test\\Escaped{Braces}"
        );
    }

    #[test]
    fn test_strip_html_tags() {
        assert_eq!(super::strip_html_tags("<p>Hello</p>"), "Hello");
        assert_eq!(
            super::strip_html_tags("<div><span>A</span> <span>B</span></div>"),
            "A B"
        );
        assert_eq!(super::strip_html_tags("No tags here"), "No tags here");
        assert_eq!(
            super::strip_html_tags("<script>alert('xss')</script>"),
            "alert('xss')"
        );
    }

    #[test]
    fn test_strip_html_tags_with_entities() {
        assert_eq!(super::strip_html_tags("&lt;hello&gt;"), "<hello>");
        assert_eq!(super::strip_html_tags("Tom &amp; Jerry"), "Tom & Jerry");
        assert_eq!(super::strip_html_tags("&quot;quoted&quot;"), "\"quoted\"");
        assert_eq!(super::strip_html_tags("It&#39;s working"), "It's working");
        assert_eq!(super::strip_html_tags("non&nbsp;breaking"), "non breaking");
    }

    #[test]
    fn test_decode_html_entities() {
        assert_eq!(super::decode_html_entities("&lt;&gt;&amp;"), "<>&");
        assert_eq!(super::decode_html_entities("&quot;&apos;"), "\"'");
        assert_eq!(super::decode_html_entities("no entities"), "no entities");
    }
}
