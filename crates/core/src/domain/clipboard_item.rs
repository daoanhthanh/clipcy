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
    lower.contains("<table") || lower.contains("<td") || lower.contains("<th")
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
        let cells = extract_table_cells(row_html);

        if !cells.is_empty() {
            rows.push(cells);
        }

        idx = row_close + 5;
    }

    if rows.is_empty() && (lower.contains("<td") || lower.contains("<th")) {
        let cells = extract_table_cells(html);
        if !cells.is_empty() {
            rows.push(cells);
        }
    }

    rows
}

fn extract_table_cells(html: &str) -> Vec<String> {
    let lower = html.to_ascii_lowercase();
    let mut cells = Vec::new();
    let mut cell_idx = 0;

    loop {
        let td_pos = lower[cell_idx..].find("<td").map(|p| p + cell_idx);
        let th_pos = lower[cell_idx..].find("<th").map(|p| p + cell_idx);
        let cell_start = match (td_pos, th_pos) {
            (Some(td), Some(th)) => td.min(th),
            (Some(td), None) => td,
            (None, Some(th)) => th,
            (None, None) => break,
        };

        let open_end = match lower[cell_start..].find('>') {
            Some(pos) => cell_start + pos + 1,
            None => break,
        };

        let closing_tag = if lower[cell_start..].starts_with("<th") {
            "</th"
        } else {
            "</td"
        };

        let close_start = match lower[open_end..].find(closing_tag) {
            Some(pos) => open_end + pos,
            None => break,
        };

        let cell_html = &html[open_end..close_start];
        let cell_text = strip_html_tags(cell_html).trim().to_string();
        cells.push(cell_text);

        cell_idx = close_start + closing_tag.len();
    }

    cells
}

/// Convert HTML content to plain text
pub fn html_to_text(html: &str) -> String {
    strip_html_tags(html)
}

fn html_to_search_text(html_format: &str) -> (String, bool) {
    let fragment =
        extract_html_fragment_from_format(html_format).unwrap_or_else(|| html_format.to_string());
    let is_table = looks_like_table_html(&fragment);
    (strip_html_tags(&fragment), is_table)
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

fn split_words(text: &str) -> Vec<String> {
    text.split(|c: char| !c.is_alphanumeric())
        .filter(|word| !word.is_empty())
        .map(|word| word.to_ascii_lowercase())
        .collect()
}

fn build_initials_aliases(words: &[String]) -> Vec<String> {
    if words.len() < 2 {
        return Vec::new();
    }

    let mut aliases = Vec::new();

    let initials: String = words
        .iter()
        .filter_map(|word| word.chars().next())
        .collect();
    if initials.len() >= 2 {
        aliases.push(initials);
    }

    let prefix: String = words
        .iter()
        .take(words.len().saturating_sub(1))
        .filter_map(|word| word.chars().next())
        .collect();
    let last = match words.last() {
        Some(word) => word,
        None => return aliases,
    };

    if !prefix.is_empty() {
        aliases.push(format!("{} {}", prefix, last));
        aliases.push(format!("{}{}", prefix, last));
    }

    aliases
}

fn build_source_app_aliases(app: &str) -> Vec<String> {
    let words = split_words(app);
    if words.is_empty() {
        return Vec::new();
    }

    let mut aliases = build_initials_aliases(&words);
    let compact = words.join("");
    if compact.len() >= 3 {
        aliases.push(compact);
    }

    aliases
}

fn build_content_aliases(text: &str) -> Vec<String> {
    let mut aliases = Vec::new();

    let first_line = text
        .lines()
        .map(|line| line.trim())
        .find(|line| !line.is_empty());
    let first_line = match first_line {
        Some(line) => line,
        None => return aliases,
    };

    if first_line.len() > 80 {
        return aliases;
    }

    let words = split_words(first_line);
    if words.len() < 2 || words.len() > 6 {
        return aliases;
    }

    aliases.extend(build_initials_aliases(&words));
    aliases
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
    /// Source application icon (small PNG)
    pub source_app_icon: Option<Vec<u8>>,
    /// Timestamp when copied
    pub created_at: DateTime<Utc>,
    /// Timestamp when last used (pasted)
    pub last_used_at: DateTime<Utc>,
    /// Whether item is pinned (won't be auto-deleted)
    pub pinned: bool,
}

impl ClipboardItem {
    /// Create new text clipboard item
    pub fn new_text(text: impl Into<String>) -> Self {
        let text = text.into();
        let now = Utc::now();

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
            source_app_icon: None,
            created_at: now,
            last_used_at: now,
            pinned: false,
        }
    }

    /// Create new image clipboard item
    pub fn new_image(png_data: Vec<u8>, thumbnail: Option<Vec<u8>>) -> Self {
        let now = Utc::now();
        Self {
            id: Uuid::new_v4(),
            content_type: ContentType::Image,
            data: png_data,
            preview: "[Image]".to_string(),
            thumbnail,
            source_app: None,
            source_app_icon: None,
            created_at: now,
            last_used_at: now,
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
        let now = Utc::now();

        Self {
            id: Uuid::new_v4(),
            content_type: ContentType::Files,
            data,
            preview,
            thumbnail: None,
            source_app: None,
            source_app_icon: None,
            created_at: now,
            last_used_at: now,
            pinned: false,
        }
    }

    /// Create new HTML clipboard item
    pub fn new_html(html: impl Into<String>) -> Self {
        let html = html.into();
        let now = Utc::now();
        let fragment = extract_html_fragment_from_format(&html).unwrap_or_else(|| html.clone());
        let preview = if looks_like_table_html(&fragment) {
            let rows = extract_table_rows(&fragment);
            if rows.is_empty() {
                // Fallback to plain text if table parsing fails
                strip_html_tags(&fragment)
                    .lines()
                    .map(|line| line.trim())
                    .find(|line| !line.is_empty())
                    .unwrap_or("Table")
                    .to_string()
            } else {
                let mut table_text = rows
                    .iter()
                    .map(|row| row.join("\t"))
                    .collect::<Vec<_>>()
                    .join("\n");

                if rows.len() == 1 && rows[0].len() == 1 {
                    table_text.push('\n');
                }

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
            source_app_icon: None,
            created_at: now,
            last_used_at: now,
            pinned: false,
        }
    }

    /// Create new Rich Text (RTF) clipboard item
    pub fn new_rtf(rtf: impl Into<String>) -> Self {
        let rtf = rtf.into();
        let now = Utc::now();
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
            source_app_icon: None,
            created_at: now,
            last_used_at: now,
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

    /// Build a searchable plain-text representation of the item
    pub fn searchable_text(&self) -> String {
        let mut parts: Vec<String> = Vec::new();

        let (mut content_text, is_table) = match self.content_type {
            ContentType::Text | ContentType::Files => {
                (String::from_utf8_lossy(&self.data).to_string(), false)
            }
            ContentType::Html => {
                let html = String::from_utf8_lossy(&self.data);
                html_to_search_text(&html)
            }
            ContentType::RichText => {
                let rtf = String::from_utf8_lossy(&self.data);
                (extract_rtf_text(&rtf), false)
            }
            ContentType::Image => (String::new(), false),
        };

        if content_text.trim().is_empty() && !self.preview.trim().is_empty() {
            content_text = self.preview.clone();
        }

        if !content_text.trim().is_empty() {
            let content_aliases = build_content_aliases(&content_text);
            parts.push(content_text);
            if !content_aliases.is_empty() {
                parts.push(content_aliases.join(" "));
            }
        }

        let mut keywords: Vec<&str> = Vec::new();
        match self.content_type {
            ContentType::Text => keywords.push("text"),
            ContentType::Html => {
                keywords.push("html");
                if is_table {
                    keywords.push("table");
                }
            }
            ContentType::RichText => {
                keywords.push("richtext");
                keywords.push("rtf");
                keywords.push("formatted");
            }
            ContentType::Image => {
                keywords.push("image");
                keywords.push("picture");
            }
            ContentType::Files => {
                keywords.push("files");
                keywords.push("file");
                keywords.push("path");
            }
        }

        if !keywords.is_empty() {
            parts.push(keywords.join(" "));
        }

        if let Some(app) = &self.source_app {
            if !app.trim().is_empty() {
                parts.push(app.clone());
            }

            let aliases = build_source_app_aliases(app);
            if !aliases.is_empty() {
                parts.push(aliases.join(" "));
            }
        }

        parts.join(" ")
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
