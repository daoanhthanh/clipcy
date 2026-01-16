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
    assert_eq!(strip_html_tags("<p>Hello</p>"), "Hello");
    assert_eq!(strip_html_tags("<div><span>A</span> <span>B</span></div>"), "A B");
    assert_eq!(strip_html_tags("No tags here"), "No tags here");
    assert_eq!(strip_html_tags("<script>alert('xss')</script>"), "alert('xss')");
}

#[test]
fn test_strip_html_tags_with_entities() {
    assert_eq!(strip_html_tags("&lt;hello&gt;"), "<hello>");
    assert_eq!(strip_html_tags("Tom &amp; Jerry"), "Tom & Jerry");
    assert_eq!(strip_html_tags("&quot;quoted&quot;"), "\"quoted\"");
    assert_eq!(strip_html_tags("It&#39;s working"), "It's working");
    assert_eq!(strip_html_tags("non&nbsp;breaking"), "non breaking");
}

#[test]
fn test_decode_html_entities() {
    assert_eq!(decode_html_entities("&lt;&gt;&amp;"), "<>&");
    assert_eq!(decode_html_entities("&quot;&apos;"), "\"'");
    assert_eq!(decode_html_entities("no entities"), "no entities");
}
