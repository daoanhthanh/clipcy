use std::mem::size_of;
use std::sync::atomic::{AtomicBool, AtomicIsize, Ordering};
use std::sync::Arc;
use std::thread;

use std::ffi::c_void;
use parking_lot::RwLock;
use windows::Win32::Foundation::{HGLOBAL, HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::Graphics::Gdi::{
    GetDIBits, GetDC, GetObjectW, ReleaseDC, BITMAP, BITMAPINFO, BITMAPINFOHEADER, BI_RGB,
    DIB_RGB_COLORS, HBITMAP,
};
use windows::Win32::System::DataExchange::*;
use windows::Win32::System::Memory::*;
use windows::Win32::System::Ole::{CF_BITMAP, CF_DIB, CF_DIBV5, CF_HDROP, CF_UNICODETEXT};
use windows::Win32::UI::Shell::DragQueryFileW;
use windows::Win32::UI::WindowsAndMessaging::*;

use clipboard_core::domain::{ClipboardItem, ContentType};
use clipboard_core::error::{ClipboardError, Result};
use clipboard_core::ports::{ClipboardCallback, ClipboardMonitor};

use once_cell::sync::Lazy;

/// Registered clipboard format for HTML Format
static CF_HTML: Lazy<u32> = Lazy::new(|| unsafe {
    RegisterClipboardFormatW(windows::core::w!("HTML Format"))
});

/// Registered clipboard format for Rich Text Format (RTF)
static CF_RTF: Lazy<u32> = Lazy::new(|| unsafe {
    RegisterClipboardFormatW(windows::core::w!("Rich Text Format"))
});

/// Registered clipboard format for PNG
static CF_PNG: Lazy<u32> = Lazy::new(|| unsafe {
    RegisterClipboardFormatW(windows::core::w!("PNG"))
});

/// PNG file signature (magic bytes)
/// See: https://www.w3.org/TR/PNG/#5PNG-file-signature
const PNG_SIGNATURE: &[u8; 8] = b"\x89PNG\r\n\x1a\n";

/// Windows clipboard monitor implementation
pub struct WindowsClipboardMonitor {
    running: Arc<AtomicBool>,
    callback: Arc<RwLock<Option<ClipboardCallback>>>,
    // Store HWND as isize to avoid Send/Sync issues with raw pointers
    hwnd: Arc<AtomicIsize>,
}

impl WindowsClipboardMonitor {
    pub fn new() -> Self {
        Self {
            running: Arc::new(AtomicBool::new(false)),
            callback: Arc::new(RwLock::new(None)),
            hwnd: Arc::new(AtomicIsize::new(0)),
        }
    }

    fn get_clipboard_text() -> Option<String> {
        unsafe {
            // Retry opening clipboard with small delays (clipboard might still be held by another app)
            let mut opened = false;
            for attempt in 0..5 {
                if OpenClipboard(None).is_ok() {
                    opened = true;
                    break;
                }
                if attempt < 4 {
                    std::thread::sleep(std::time::Duration::from_millis(50));
                }
            }

            if !opened {
                return None;
            }

            let handle = GetClipboardData(CF_UNICODETEXT.0 as u32);
            if handle.is_err() {
                let _ = CloseClipboard();
                return None;
            }

            let handle = handle.unwrap();
            if handle.0.is_null() {
                let _ = CloseClipboard();
                return None;
            }

            let hglobal = HGLOBAL(handle.0);
            let ptr = GlobalLock(hglobal);
            if ptr.is_null() {
                let _ = CloseClipboard();
                return None;
            }

            // Read wide string
            let mut len = 0;
            let mut p = ptr as *const u16;
            while *p != 0 {
                len += 1;
                p = p.add(1);
            }

            let slice = std::slice::from_raw_parts(ptr as *const u16, len);
            let text = String::from_utf16_lossy(slice);

            let _ = GlobalUnlock(hglobal);
            let _ = CloseClipboard();

            Some(text)
        }
    }

    fn get_clipboard_image() -> Option<(Vec<u8>, Option<Vec<u8>>)> {
        unsafe {
            // Retry opening clipboard with small delays (clipboard might still be held by another app)
            let mut opened = false;
            for attempt in 0..5 {
                if OpenClipboard(None).is_ok() {
                    opened = true;
                    break;
                }
                if attempt < 4 {
                    std::thread::sleep(std::time::Duration::from_millis(50));
                }
            }

            if !opened {
                tracing::debug!("get_clipboard_image: OpenClipboard failed after retries");
                return None;
            }

            // Try CF_DIB / CF_DIBV5 first (Device Independent Bitmap)
            let mut result = None;

            if let Ok(handle) = GetClipboardData(CF_DIB.0 as u32)
                .or_else(|_| GetClipboardData(CF_DIBV5.0 as u32))
            {
                if !handle.0.is_null() {
                    let hglobal = HGLOBAL(handle.0);
                    let ptr = GlobalLock(hglobal);
                    if !ptr.is_null() {
                        let size = GlobalSize(hglobal);
                        if size > 0 {
                            tracing::debug!("get_clipboard_image: DIB data size = {} bytes", size);
                            let dib_data = std::slice::from_raw_parts(ptr as *const u8, size);
                            if let Some(png) = dib_to_png(dib_data) {
                                tracing::debug!(
                                    "get_clipboard_image: PNG conversion succeeded, {} bytes",
                                    png.len()
                                );
                                let thumbnail = generate_thumbnail(&png, 64, 64);
                                result = Some((png, thumbnail));
                            } else {
                                tracing::debug!("get_clipboard_image: dib_to_png failed");
                            }
                        }
                        let _ = GlobalUnlock(hglobal);
                    }
                }
            }

            // Fallback to CF_BITMAP for sources (like default screenshots) that only expose bitmaps
            if result.is_none() {
                if let Ok(handle) = GetClipboardData(CF_BITMAP.0 as u32) {
                    if !handle.0.is_null() {
                        result = get_png_from_bitmap(HBITMAP(handle.0));
                    }
                }
            }

            let _ = CloseClipboard();
            result
        }
    }

    fn has_image_format() -> bool {
        unsafe {
            // IsClipboardFormatAvailable doesn't require OpenClipboard
            let has_png = IsClipboardFormatAvailable(*CF_PNG).is_ok();
            let has_dib = IsClipboardFormatAvailable(CF_DIB.0 as u32).is_ok();
            let has_dib_v5 = IsClipboardFormatAvailable(CF_DIBV5.0 as u32).is_ok();
            let has_bitmap = IsClipboardFormatAvailable(CF_BITMAP.0 as u32).is_ok();
            has_png || has_dib || has_dib_v5 || has_bitmap
        }
    }

    fn has_text_format() -> bool {
        unsafe {
            // IsClipboardFormatAvailable doesn't require OpenClipboard
            IsClipboardFormatAvailable(CF_UNICODETEXT.0 as u32).is_ok()
        }
    }

    fn has_files_format() -> bool {
        unsafe {
            IsClipboardFormatAvailable(CF_HDROP.0 as u32).is_ok()
        }
    }

    fn has_html_format() -> bool {
        unsafe {
            IsClipboardFormatAvailable(*CF_HTML).is_ok()
        }
    }

    fn has_rtf_format() -> bool {
        unsafe {
            IsClipboardFormatAvailable(*CF_RTF).is_ok()
        }
    }

    fn has_png_format() -> bool {
        unsafe {
            IsClipboardFormatAvailable(*CF_PNG).is_ok()
        }
    }

    /// Get file paths from CF_HDROP format
    fn get_clipboard_files() -> Option<Vec<String>> {
        unsafe {
            // Retry opening clipboard with small delays
            let mut opened = false;
            for attempt in 0..5 {
                if OpenClipboard(None).is_ok() {
                    opened = true;
                    break;
                }
                if attempt < 4 {
                    std::thread::sleep(std::time::Duration::from_millis(50));
                }
            }

            if !opened {
                tracing::debug!("get_clipboard_files: OpenClipboard failed after retries");
                return None;
            }

            let handle = GetClipboardData(CF_HDROP.0 as u32);
            if handle.is_err() {
                let _ = CloseClipboard();
                return None;
            }

            let handle = handle.unwrap();
            if handle.0.is_null() {
                let _ = CloseClipboard();
                return None;
            }

            // HDROP is a handle to a DROPFILES structure
            let hdrop = windows::Win32::UI::Shell::HDROP(handle.0);

            // Get the number of files
            let file_count = DragQueryFileW(hdrop, 0xFFFFFFFF, None);

            let mut files = Vec::with_capacity(file_count as usize);

            for i in 0..file_count {
                // Get required buffer size
                let len = DragQueryFileW(hdrop, i, None);
                if len == 0 {
                    continue;
                }

                // Allocate buffer and get file path
                let mut buffer: Vec<u16> = vec![0u16; (len + 1) as usize];
                let actual_len = DragQueryFileW(hdrop, i, Some(&mut buffer));
                if actual_len > 0 {
                    let path = String::from_utf16_lossy(&buffer[..actual_len as usize]);
                    files.push(path);
                }
            }

            let _ = CloseClipboard();

            if files.is_empty() {
                None
            } else {
                tracing::debug!("get_clipboard_files: retrieved {} files", files.len());
                Some(files)
            }
        }
    }

    /// Get HTML content from clipboard
    fn get_clipboard_html() -> Option<String> {
        unsafe {
            // Retry opening clipboard with small delays
            let mut opened = false;
            for attempt in 0..5 {
                if OpenClipboard(None).is_ok() {
                    opened = true;
                    break;
                }
                if attempt < 4 {
                    std::thread::sleep(std::time::Duration::from_millis(50));
                }
            }

            if !opened {
                return None;
            }

            let handle = GetClipboardData(*CF_HTML);
            if handle.is_err() {
                let _ = CloseClipboard();
                return None;
            }

            let handle = handle.unwrap();
            if handle.0.is_null() {
                let _ = CloseClipboard();
                return None;
            }

            let hglobal = HGLOBAL(handle.0);
            let ptr = GlobalLock(hglobal);
            if ptr.is_null() {
                let _ = CloseClipboard();
                return None;
            }

            // HTML Format is stored as UTF-8 encoded string with headers
            let size = GlobalSize(hglobal);
            if size == 0 {
                let _ = GlobalUnlock(hglobal);
                let _ = CloseClipboard();
                return None;
            }

            let bytes = std::slice::from_raw_parts(ptr as *const u8, size);
            // Find the null terminator
            let len = bytes.iter().position(|&b| b == 0).unwrap_or(bytes.len());
            let html_str = String::from_utf8_lossy(&bytes[..len]).to_string();

            let _ = GlobalUnlock(hglobal);
            let _ = CloseClipboard();

            // Parse HTML Format to extract the actual HTML content
            // The format includes headers like:
            // Version:0.9
            // StartHTML:xxx
            // EndHTML:xxx
            // StartFragment:xxx
            // EndFragment:xxx
            if let Some(html_content) = extract_html_fragment(&html_str) {
                tracing::debug!("get_clipboard_html: extracted {} chars", html_content.len());
                Some(html_content)
            } else {
                // If parsing fails, return the whole content
                tracing::debug!("get_clipboard_html: returning raw content {} chars", html_str.len());
                Some(html_str)
            }
        }
    }

    /// Get RTF content from clipboard
    fn get_clipboard_rtf() -> Option<String> {
        unsafe {
            // Retry opening clipboard with small delays
            let mut opened = false;
            for attempt in 0..5 {
                if OpenClipboard(None).is_ok() {
                    opened = true;
                    break;
                }
                if attempt < 4 {
                    std::thread::sleep(std::time::Duration::from_millis(50));
                }
            }

            if !opened {
                return None;
            }

            let handle = GetClipboardData(*CF_RTF);
            if handle.is_err() {
                let _ = CloseClipboard();
                return None;
            }

            let handle = handle.unwrap();
            if handle.0.is_null() {
                let _ = CloseClipboard();
                return None;
            }

            let hglobal = HGLOBAL(handle.0);
            let ptr = GlobalLock(hglobal);
            if ptr.is_null() {
                let _ = CloseClipboard();
                return None;
            }

            let size = GlobalSize(hglobal);
            if size == 0 {
                let _ = GlobalUnlock(hglobal);
                let _ = CloseClipboard();
                return None;
            }

            // RTF is stored as ASCII/UTF-8 string
            let bytes = std::slice::from_raw_parts(ptr as *const u8, size);
            let len = bytes.iter().position(|&b| b == 0).unwrap_or(bytes.len());
            let rtf_str = String::from_utf8_lossy(&bytes[..len]).to_string();

            let _ = GlobalUnlock(hglobal);
            let _ = CloseClipboard();

            tracing::debug!("get_clipboard_rtf: retrieved {} chars", rtf_str.len());
            Some(rtf_str)
        }
    }

    /// Get PNG image data directly from clipboard
    fn get_clipboard_png() -> Option<(Vec<u8>, Option<Vec<u8>>)> {
        unsafe {
            // Retry opening clipboard with small delays
            let mut opened = false;
            for attempt in 0..5 {
                if OpenClipboard(None).is_ok() {
                    opened = true;
                    break;
                }
                if attempt < 4 {
                    std::thread::sleep(std::time::Duration::from_millis(50));
                }
            }

            if !opened {
                tracing::debug!("get_clipboard_png: OpenClipboard failed after retries");
                return None;
            }

            let handle = GetClipboardData(*CF_PNG);
            if handle.is_err() {
                let _ = CloseClipboard();
                return None;
            }

            let handle = handle.unwrap();
            if handle.0.is_null() {
                let _ = CloseClipboard();
                return None;
            }

            let hglobal = HGLOBAL(handle.0);
            let ptr = GlobalLock(hglobal);
            if ptr.is_null() {
                let _ = CloseClipboard();
                return None;
            }

            let size = GlobalSize(hglobal);
            if size == 0 {
                let _ = GlobalUnlock(hglobal);
                let _ = CloseClipboard();
                return None;
            }

            let png_data = std::slice::from_raw_parts(ptr as *const u8, size).to_vec();

            let _ = GlobalUnlock(hglobal);
            let _ = CloseClipboard();

            // Validate PNG signature
            if png_data.len() < PNG_SIGNATURE.len() || &png_data[..PNG_SIGNATURE.len()] != PNG_SIGNATURE {
                tracing::debug!("get_clipboard_png: invalid PNG signature");
                return None;
            }

            tracing::debug!("get_clipboard_png: retrieved {} bytes", png_data.len());
            let thumbnail = generate_thumbnail(&png_data, 64, 64);
            Some((png_data, thumbnail))
        }
    }

    /// Get current clipboard item, with prioritized format detection
    /// Priority order: Files (unless single image file with image data available) > Images (PNG > CF_DIB/CF_DIBV5 > CF_BITMAP) > HTML Format > Rich Text Format > CF_UNICODETEXT
    pub fn get_current_item() -> Option<ClipboardItem> {
        // Check available formats
        let has_files = Self::has_files_format();
        let has_image = Self::has_image_format();
        let has_html = Self::has_html_format();
        let has_rtf = Self::has_rtf_format();
        let has_text = Self::has_text_format();

        tracing::debug!(
            "Clipboard formats - files: {}, image: {}, html: {}, rtf: {}, text: {}",
            has_files, has_image, has_html, has_rtf, has_text
        );

        // Priority 1: File drops (CF_HDROP)
        // But if we have both files and image formats, and the file is a single image file
        // (like from Windows Snipping Tool), prefer the image format for better preview
        if has_files {
            if let Some(files) = Self::get_clipboard_files() {
                // Check if this is a single image file with image data also available
                // Windows Snipping Tool puts screenshots in a temp folder and provides both CF_HDROP and CF_DIB
                let should_prefer_image = has_image && files.len() == 1 && {
                    let path = &files[0];
                    let lower = path.to_lowercase();
                    // Check if it's an image file extension
                    let is_image_extension = lower.ends_with(".png")
                        || lower.ends_with(".jpg")
                        || lower.ends_with(".jpeg")
                        || lower.ends_with(".bmp")
                        || lower.ends_with(".gif");
                    // Check if it's from Windows temp/screenshot locations
                    let is_temp_location = lower.contains("\\appdata\\local\\")
                        || lower.contains("\\temp\\")
                        || lower.contains("\\tmp\\")
                        || lower.contains("screenclip");
                    is_image_extension && is_temp_location
                };

                if should_prefer_image {
                    tracing::info!(
                        "Detected single image file from temp location with image data available, preferring image format: {}",
                        &files[0]
                    );
                    // Fall through to image handling below
                } else {
                    tracing::info!("Captured {} files", files.len());
                    return Some(ClipboardItem::new_files(files));
                }
            } else {
                tracing::warn!("has_files_format() returned true but get_clipboard_files() failed");
            }
        }

        // Priority 2: Images (try PNG first, then DIB/Bitmap)
        if has_image {
            // Try PNG format first (higher quality, no conversion needed)
            if Self::has_png_format() {
                if let Some((png_data, thumbnail)) = Self::get_clipboard_png() {
                    tracing::info!("Captured PNG image: {} bytes", png_data.len());
                    return Some(ClipboardItem::new_image(png_data, thumbnail));
                }
            }

            // Fall back to DIB/Bitmap formats
            if let Some((png_data, thumbnail)) = Self::get_clipboard_image() {
                tracing::info!("Captured image: {} bytes, thumbnail: {:?}", png_data.len(), thumbnail.as_ref().map(|t| t.len()));
                return Some(ClipboardItem::new_image(png_data, thumbnail));
            } else {
                tracing::warn!("has_image_format() returned true but get_clipboard_image() failed");
            }
        }

        // Priority 3: HTML content
        if has_html {
            if let Some(html) = Self::get_clipboard_html() {
                if !html.trim().is_empty() {
                    tracing::debug!("Captured HTML: {} chars", html.len());
                    return Some(ClipboardItem::new_html(html));
                }
            }
        }

        // Priority 4: RTF content
        if has_rtf {
            if let Some(rtf) = Self::get_clipboard_rtf() {
                if !rtf.trim().is_empty() {
                    tracing::debug!("Captured RTF: {} chars", rtf.len());
                    return Some(ClipboardItem::new_rtf(rtf));
                }
            }
        }

        // Priority 5: Plain text (fallback)
        if has_text {
            if let Some(text) = Self::get_clipboard_text() {
                // Skip whitespace-only content
                if text.trim().is_empty() {
                    tracing::debug!("Skipping whitespace-only clipboard content");
                    return None;
                }
                tracing::debug!("Captured text: {} chars", text.len());
                return Some(ClipboardItem::new_text(text));
            }
        }

        None
    }
}

/// Parse an offset value from HTML Format header
/// Returns None if the header is not found or cannot be parsed
fn parse_html_format_offset(html_format: &str, prefix: &str) -> Option<usize> {
    html_format
        .lines()
        .find(|l| l.starts_with(prefix))
        .and_then(|l| l.strip_prefix(prefix))
        .and_then(|s| s.trim().parse::<usize>().ok())
}

/// Extract HTML content from Windows HTML Format clipboard data
/// The format includes headers like:
/// Version:0.9
/// StartHTML:xxx
/// EndHTML:xxx
/// StartFragment:xxx
/// EndFragment:xxx
fn extract_html_fragment(html_format: &str) -> Option<String> {
    // Try to find StartFragment and EndFragment markers first (most specific)
    let start_frag = parse_html_format_offset(html_format, "StartFragment:");
    let end_frag = parse_html_format_offset(html_format, "EndFragment:");

    if let (Some(start), Some(end)) = (start_frag, end_frag) {
        if start < end && end <= html_format.len() {
            return Some(html_format[start..end].to_string());
        }
    }

    // Fallback: try StartHTML/EndHTML
    let start_html = parse_html_format_offset(html_format, "StartHTML:");
    let end_html = parse_html_format_offset(html_format, "EndHTML:");

    if let (Some(start), Some(end)) = (start_html, end_html) {
        if start < end && end <= html_format.len() {
            return Some(html_format[start..end].to_string());
        }
    }

    None
}

/// Create HDROP data structure for file paths
/// DROPFILES structure:
/// - pFiles: DWORD - offset to file list
/// - pt: POINT - drop point (we use 0,0)
/// - fNC: BOOL - non-client area flag
/// - fWide: BOOL - true for Unicode
fn create_hdrop_data(paths: &[&str]) -> Option<Vec<u8>> {
    if paths.is_empty() {
        return None;
    }

    // DROPFILES header size (20 bytes)
    // DROPFILES structure header size in bytes:
    // - pFiles: DWORD (4 bytes) - offset to file list from start of structure
    // - pt: POINT (8 bytes) - drop point coordinates (pt.x: 4, pt.y: 4)
    // - fNC: BOOL (4 bytes) - non-client area flag
    // - fWide: BOOL (4 bytes) - Unicode flag (TRUE for wide char paths)
    // Total: 4 + 4 + 4 + 4 + 4 = 20 bytes
    const DROPFILES_SIZE: usize = 20;

    // Calculate total size
    let mut total_size = DROPFILES_SIZE;
    for path in paths {
        // Each path as wide string + null terminator
        total_size += (path.encode_utf16().count() + 1) * 2;
    }
    // Final double null terminator
    total_size += 2;

    let mut data = Vec::with_capacity(total_size);

    // pFiles - offset to file list (DROPFILES_SIZE)
    data.extend_from_slice(&(DROPFILES_SIZE as u32).to_le_bytes());
    // pt.x
    data.extend_from_slice(&0i32.to_le_bytes());
    // pt.y
    data.extend_from_slice(&0i32.to_le_bytes());
    // fNC - non-client area flag (FALSE)
    data.extend_from_slice(&0u32.to_le_bytes());
    // fWide - Unicode flag (TRUE)
    data.extend_from_slice(&1u32.to_le_bytes());

    // File paths as null-terminated wide strings
    for path in paths {
        for wchar in path.encode_utf16() {
            data.extend_from_slice(&wchar.to_le_bytes());
        }
        // Null terminator
        data.extend_from_slice(&0u16.to_le_bytes());
    }

    // Final double null terminator
    data.extend_from_slice(&0u16.to_le_bytes());

    Some(data)
}

/// Create HTML Format clipboard data from HTML content
/// Windows HTML Format requires specific headers with byte offsets
fn create_html_format(html: &str) -> String {
    // Template for HTML Format header
    const VERSION: &str = "Version:0.9\r\n";
    const START_HTML: &str = "StartHTML:";
    const END_HTML: &str = "EndHTML:";
    const START_FRAGMENT: &str = "StartFragment:";
    const END_FRAGMENT: &str = "EndFragment:";

    // Calculate header length with placeholder digits
    // We use 10 digits for offsets to have enough room
    let header_template = format!(
        "{}{}0000000000\r\n{}0000000000\r\n{}0000000000\r\n{}0000000000\r\n",
        VERSION, START_HTML, END_HTML, START_FRAGMENT, END_FRAGMENT
    );

    let header_len = header_template.len();

    // HTML content with fragment markers
    let html_start = "<!--StartFragment-->";
    let html_end = "<!--EndFragment-->";
    let full_html = format!("{}{}{}", html_start, html, html_end);

    let start_html_offset = header_len;
    let end_html_offset = header_len + full_html.len();
    let start_fragment_offset = header_len + html_start.len();
    let end_fragment_offset = header_len + html_start.len() + html.len();

    format!(
        "{}{}:{:010}\r\n{}:{:010}\r\n{}:{:010}\r\n{}:{:010}\r\n{}",
        VERSION,
        START_HTML, start_html_offset,
        END_HTML, end_html_offset,
        START_FRAGMENT, start_fragment_offset,
        END_FRAGMENT, end_fragment_offset,
        full_html
    )
}

/// Convert DIB (Device Independent Bitmap) data to PNG format
fn dib_to_png(dib_data: &[u8]) -> Option<Vec<u8>> {
    if dib_data.len() < 40 {
        tracing::debug!("dib_to_png: data too small ({} bytes)", dib_data.len());
        return None;
    }

    // Parse BITMAPINFOHEADER
    let width = i32::from_le_bytes([dib_data[4], dib_data[5], dib_data[6], dib_data[7]]);
    let height = i32::from_le_bytes([dib_data[8], dib_data[9], dib_data[10], dib_data[11]]);
    let bit_count = u16::from_le_bytes([dib_data[14], dib_data[15]]);
    let compression = u32::from_le_bytes([dib_data[16], dib_data[17], dib_data[18], dib_data[19]]);

    tracing::debug!("dib_to_png: width={}, height={}, bit_count={}, compression={}", width, height, bit_count, compression);

    if width <= 0 || height == 0 {
        tracing::debug!("dib_to_png: invalid dimensions");
        return None;
    }

    let abs_height = height.unsigned_abs() as usize;
    let width = width as usize;
    let is_top_down = height < 0;

    // Only support uncompressed bitmaps for now
    if compression != 0 {
        tracing::debug!("Unsupported DIB compression: {}", compression);
        return None;
    }

    // Calculate header size and pixel data offset
    let header_size = u32::from_le_bytes([dib_data[0], dib_data[1], dib_data[2], dib_data[3]]) as usize;

    // Color table size for <= 8 bit images
    let color_table_size = if bit_count <= 8 {
        (1 << bit_count) * 4
    } else {
        0
    };

    let pixel_offset = header_size + color_table_size;

    if pixel_offset >= dib_data.len() {
        return None;
    }

    let pixel_data = &dib_data[pixel_offset..];

    // Convert to RGBA based on bit depth
    let rgba_data = match bit_count {
        24 => convert_bgr24_to_rgba(pixel_data, width, abs_height, is_top_down),
        32 => convert_bgra32_to_rgba(pixel_data, width, abs_height, is_top_down),
        _ => {
            tracing::debug!("Unsupported DIB bit depth: {}", bit_count);
            return None;
        }
    };

    let rgba_data = rgba_data?;

    // Encode as PNG
    encode_png(&rgba_data, width as u32, abs_height as u32)
}

fn convert_bgr24_to_rgba(data: &[u8], width: usize, height: usize, is_top_down: bool) -> Option<Vec<u8>> {
    // Row stride must be aligned to 4 bytes
    let row_stride = ((width * 3 + 3) / 4) * 4;
    let expected_size = row_stride * height;

    if data.len() < expected_size {
        return None;
    }

    let mut rgba = vec![0u8; width * height * 4];

    for y in 0..height {
        let src_y = if is_top_down { y } else { height - 1 - y };
        let src_row = &data[src_y * row_stride..];

        for x in 0..width {
            let src_idx = x * 3;
            let dst_idx = (y * width + x) * 4;

            rgba[dst_idx] = src_row[src_idx + 2];     // R (from B)
            rgba[dst_idx + 1] = src_row[src_idx + 1]; // G
            rgba[dst_idx + 2] = src_row[src_idx];     // B (from R)
            rgba[dst_idx + 3] = 255;                   // A
        }
    }

    Some(rgba)
}

fn convert_bgra32_to_rgba(data: &[u8], width: usize, height: usize, is_top_down: bool) -> Option<Vec<u8>> {
    let row_stride = width * 4;
    let expected_size = row_stride * height;

    if data.len() < expected_size {
        return None;
    }

    let mut rgba = vec![0u8; width * height * 4];

    for y in 0..height {
        let src_y = if is_top_down { y } else { height - 1 - y };
        let src_row = &data[src_y * row_stride..];

        for x in 0..width {
            let src_idx = x * 4;
            let dst_idx = (y * width + x) * 4;

            rgba[dst_idx] = src_row[src_idx + 2];     // R (from B)
            rgba[dst_idx + 1] = src_row[src_idx + 1]; // G
            rgba[dst_idx + 2] = src_row[src_idx];     // B (from R)
            rgba[dst_idx + 3] = src_row[src_idx + 3]; // A
        }
    }

    Some(rgba)
}

fn encode_png(rgba_data: &[u8], width: u32, height: u32) -> Option<Vec<u8>> {
    use image::{ImageBuffer, Rgba};

    let img: ImageBuffer<Rgba<u8>, Vec<u8>> = ImageBuffer::from_raw(width, height, rgba_data.to_vec())?;

    let mut png_bytes = Vec::new();
    let encoder = image::codecs::png::PngEncoder::new(&mut png_bytes);

    if encoder.encode(
        img.as_raw(),
        width,
        height,
        image::ColorType::Rgba8,
    ).is_ok() {
        Some(png_bytes)
    } else {
        None
    }
}

fn generate_thumbnail(png_data: &[u8], max_width: u32, max_height: u32) -> Option<Vec<u8>> {
    use image::io::Reader as ImageReader;
    use std::io::Cursor;

    let img = ImageReader::new(Cursor::new(png_data))
        .with_guessed_format()
        .ok()?
        .decode()
        .ok()?;

    let thumbnail = img.thumbnail(max_width, max_height);

    let mut png_bytes = Vec::new();
    let encoder = image::codecs::png::PngEncoder::new(&mut png_bytes);

    if encoder.encode(
        thumbnail.as_bytes(),
        thumbnail.width(),
        thumbnail.height(),
        image::ColorType::Rgba8,
    ).is_ok() {
        Some(png_bytes)
    } else {
        None
    }
}

impl Default for WindowsClipboardMonitor {
    fn default() -> Self {
        Self::new()
    }
}

unsafe impl Send for WindowsClipboardMonitor {}
unsafe impl Sync for WindowsClipboardMonitor {}

impl ClipboardMonitor for WindowsClipboardMonitor {
    fn start(&self) -> Result<()> {
        if self.running.load(Ordering::SeqCst) {
            return Ok(());
        }

        self.running.store(true, Ordering::SeqCst);

        let running = Arc::clone(&self.running);
        let callback = Arc::clone(&self.callback);
        let hwnd_store = Arc::clone(&self.hwnd);

        thread::spawn(move || {
            unsafe {
                // Create hidden window for clipboard messages
                let class_name = windows::core::w!("ClipboardMonitorClass");

                let wc = WNDCLASSEXW {
                    cbSize: size_of::<WNDCLASSEXW>() as u32,
                    lpfnWndProc: Some(window_proc),
                    lpszClassName: class_name,
                    ..Default::default()
                };

                RegisterClassExW(&wc);

                let hwnd = match CreateWindowExW(
                    WINDOW_EX_STYLE::default(),
                    class_name,
                    windows::core::w!("ClipboardMonitor"),
                    WS_OVERLAPPED,
                    0, 0, 0, 0,
                    None,
                    None,
                    None,
                    None,
                ) {
                    Ok(h) => h,
                    Err(e) => {
                        tracing::error!("Failed to create clipboard monitor window: {}", e);
                        return;
                    }
                };

                hwnd_store.store(hwnd.0 as isize, Ordering::SeqCst);

                // Register as clipboard listener
                let _ = AddClipboardFormatListener(hwnd);

                // Store context in window
                let ctx = Box::new((callback.clone(), running.clone()));
                SetWindowLongPtrW(hwnd, GWLP_USERDATA, Box::into_raw(ctx) as isize);

                // Message loop
                let mut msg = MSG::default();
                while running.load(Ordering::SeqCst) {
                    if PeekMessageW(&mut msg, Some(hwnd), 0, 0, PM_REMOVE).as_bool() {
                        let _ = TranslateMessage(&msg);
                        DispatchMessageW(&msg);
                    }
                    thread::sleep(std::time::Duration::from_millis(10));
                }

                // Cleanup
                let _ = RemoveClipboardFormatListener(hwnd);
                let _ = DestroyWindow(hwnd);
            }
        });

        Ok(())
    }

    fn stop(&self) -> Result<()> {
        self.running.store(false, Ordering::SeqCst);
        Ok(())
    }

    fn is_running(&self) -> bool {
        self.running.load(Ordering::SeqCst)
    }

    fn on_change(&self, callback: ClipboardCallback) {
        *self.callback.write() = Some(callback);
    }

    fn get_current(&self) -> Result<Option<ClipboardItem>> {
        Ok(Self::get_current_item())
    }

    fn set_content(&self, item: &ClipboardItem) -> Result<()> {
        match item.content_type {
            ContentType::Text => {
                unsafe {
                    if OpenClipboard(None).is_err() {
                        return Err(ClipboardError::ClipboardAccess("Failed to open clipboard".to_string()));
                    }

                    let _ = EmptyClipboard();

                    // Convert to wide string
                    let text = String::from_utf8_lossy(&item.data);
                    let wide: Vec<u16> = text.encode_utf16().chain(std::iter::once(0)).collect();
                    let size = wide.len() * 2;

                    let handle = GlobalAlloc(GMEM_MOVEABLE, size);
                    if handle.is_err() {
                        let _ = CloseClipboard();
                        return Err(ClipboardError::ClipboardAccess("Failed to allocate memory".to_string()));
                    }

                    let handle = handle.unwrap();
                    let ptr = GlobalLock(handle);
                    std::ptr::copy_nonoverlapping(wide.as_ptr(), ptr as *mut u16, wide.len());
                    let _ = GlobalUnlock(handle);

                    let _ = SetClipboardData(CF_UNICODETEXT.0 as u32, Some(windows::Win32::Foundation::HANDLE(handle.0)));

                    let _ = CloseClipboard();
                }
                Ok(())
            }
            ContentType::Image => {
                // Set image to clipboard by converting PNG to DIB
                unsafe {
                    if OpenClipboard(None).is_err() {
                        return Err(ClipboardError::ClipboardAccess("Failed to open clipboard".to_string()));
                    }

                    let _ = EmptyClipboard();

                    // Convert PNG to DIB
                    if let Some(dib_data) = png_to_dib(&item.data) {
                        let handle = GlobalAlloc(GMEM_MOVEABLE, dib_data.len());
                        if let Ok(handle) = handle {
                            let ptr = GlobalLock(handle);
                            std::ptr::copy_nonoverlapping(dib_data.as_ptr(), ptr as *mut u8, dib_data.len());
                            let _ = GlobalUnlock(handle);
                            let _ = SetClipboardData(CF_DIB.0 as u32, Some(windows::Win32::Foundation::HANDLE(handle.0)));
                        }
                    }

                    let _ = CloseClipboard();
                }
                Ok(())
            }
            ContentType::Files => {
                // Set files to clipboard using CF_HDROP format
                unsafe {
                    if OpenClipboard(None).is_err() {
                        return Err(ClipboardError::ClipboardAccess("Failed to open clipboard".to_string()));
                    }

                    let _ = EmptyClipboard();

                    // Parse file paths from data
                    let paths_str = String::from_utf8_lossy(&item.data);
                    let paths: Vec<&str> = paths_str.lines().collect();

                    if let Some(hdrop_data) = create_hdrop_data(&paths) {
                        let handle = GlobalAlloc(GMEM_MOVEABLE, hdrop_data.len());
                        if let Ok(handle) = handle {
                            let ptr = GlobalLock(handle);
                            std::ptr::copy_nonoverlapping(hdrop_data.as_ptr(), ptr as *mut u8, hdrop_data.len());
                            let _ = GlobalUnlock(handle);
                            let _ = SetClipboardData(CF_HDROP.0 as u32, Some(windows::Win32::Foundation::HANDLE(handle.0)));
                        }
                    }

                    let _ = CloseClipboard();
                }
                Ok(())
            }
            ContentType::Html => {
                // Set HTML to clipboard using HTML Format
                unsafe {
                    if OpenClipboard(None).is_err() {
                        return Err(ClipboardError::ClipboardAccess("Failed to open clipboard".to_string()));
                    }

                    let _ = EmptyClipboard();

                    let html_str = String::from_utf8_lossy(&item.data);
                    let html_format = create_html_format(&html_str);
                    let data = html_format.as_bytes();

                    let handle = GlobalAlloc(GMEM_MOVEABLE, data.len() + 1);
                    if let Ok(handle) = handle {
                        let ptr = GlobalLock(handle);
                        std::ptr::copy_nonoverlapping(data.as_ptr(), ptr as *mut u8, data.len());
                        // Null terminate
                        *((ptr as *mut u8).add(data.len())) = 0;
                        let _ = GlobalUnlock(handle);
                        let _ = SetClipboardData(*CF_HTML, Some(windows::Win32::Foundation::HANDLE(handle.0)));
                    }

                    let _ = CloseClipboard();
                }
                Ok(())
            }
            ContentType::RichText => {
                // Set RTF to clipboard
                unsafe {
                    if OpenClipboard(None).is_err() {
                        return Err(ClipboardError::ClipboardAccess("Failed to open clipboard".to_string()));
                    }

                    let _ = EmptyClipboard();

                    let data = &item.data;
                    let handle = GlobalAlloc(GMEM_MOVEABLE, data.len() + 1);
                    if let Ok(handle) = handle {
                        let ptr = GlobalLock(handle);
                        std::ptr::copy_nonoverlapping(data.as_ptr(), ptr as *mut u8, data.len());
                        // Null terminate
                        *((ptr as *mut u8).add(data.len())) = 0;
                        let _ = GlobalUnlock(handle);
                        let _ = SetClipboardData(*CF_RTF, Some(windows::Win32::Foundation::HANDLE(handle.0)));
                    }

                    let _ = CloseClipboard();
                }
                Ok(())
            }
        }
    }
}

/// Convert PNG data to DIB format for clipboard
fn png_to_dib(png_data: &[u8]) -> Option<Vec<u8>> {
    use image::io::Reader as ImageReader;
    use std::io::Cursor;

    let img = ImageReader::new(Cursor::new(png_data))
        .with_guessed_format()
        .ok()?
        .decode()
        .ok()?;

    let rgba = img.to_rgba8();
    let width = rgba.width() as usize;
    let height = rgba.height() as usize;

    // Create BITMAPINFOHEADER (40 bytes)
    let mut dib = Vec::with_capacity(40 + width * height * 4);

    // biSize
    dib.extend_from_slice(&40u32.to_le_bytes());
    // biWidth
    dib.extend_from_slice(&(width as i32).to_le_bytes());
    // biHeight (positive = bottom-up)
    dib.extend_from_slice(&(height as i32).to_le_bytes());
    // biPlanes
    dib.extend_from_slice(&1u16.to_le_bytes());
    // biBitCount
    dib.extend_from_slice(&32u16.to_le_bytes());
    // biCompression (BI_RGB = 0)
    dib.extend_from_slice(&0u32.to_le_bytes());
    // biSizeImage
    dib.extend_from_slice(&((width * height * 4) as u32).to_le_bytes());
    // biXPelsPerMeter
    dib.extend_from_slice(&0i32.to_le_bytes());
    // biYPelsPerMeter
    dib.extend_from_slice(&0i32.to_le_bytes());
    // biClrUsed
    dib.extend_from_slice(&0u32.to_le_bytes());
    // biClrImportant
    dib.extend_from_slice(&0u32.to_le_bytes());

    // Pixel data (bottom-up, BGRA format)
    for y in (0..height).rev() {
        for x in 0..width {
            let pixel = rgba.get_pixel(x as u32, y as u32);
            dib.push(pixel[2]); // B
            dib.push(pixel[1]); // G
            dib.push(pixel[0]); // R
            dib.push(pixel[3]); // A
        }
    }

    Some(dib)
}

/// Convert an HBITMAP clipboard handle into PNG data (with thumbnail)
fn get_png_from_bitmap(hbitmap: HBITMAP) -> Option<(Vec<u8>, Option<Vec<u8>>)> {
    unsafe {
        let mut bitmap = BITMAP::default();
        if GetObjectW(
            hbitmap.into(),
            size_of::<BITMAP>() as i32,
            Some(&mut bitmap as *mut _ as *mut c_void),
        ) == 0
        {
            tracing::debug!("get_clipboard_image: GetObjectW failed");
            return None;
        }

        let width = bitmap.bmWidth;
        let height = bitmap.bmHeight;
        if width <= 0 || height == 0 {
            tracing::debug!("get_clipboard_image: invalid bitmap dimensions");
            return None;
        }

        let abs_height: u32 = height.unsigned_abs();
        let is_top_down = height < 0;

        let mut info = BITMAPINFO {
            bmiHeader: BITMAPINFOHEADER {
                biSize: size_of::<BITMAPINFOHEADER>() as u32,
                biWidth: width,
                biHeight: height,
                biPlanes: 1,
                biBitCount: 32,
                biCompression: BI_RGB.0 as u32,
                ..Default::default()
            },
            ..Default::default()
        };

        const BYTES_PER_PIXEL_RGBA: usize = 4;
        let buffer_size = (width as usize)
            .checked_mul(BYTES_PER_PIXEL_RGBA)
            .and_then(|v| v.checked_mul(abs_height as usize));
        let buffer_size = match buffer_size {
            Some(size) if size > 0 => size,
            _ => {
                tracing::debug!("get_clipboard_image: buffer_size overflow/zero");
                return None;
            }
        };
        let mut buffer = vec![0u8; buffer_size];

        let hdc = GetDC(None);
        if hdc.0.is_null() {
            tracing::debug!("get_clipboard_image: GetDC failed");
            return None;
        }

        let scan_lines = GetDIBits(
            hdc,
            hbitmap,
            0,
            abs_height,
            Some(buffer.as_mut_ptr() as *mut c_void),
            &mut info,
            DIB_RGB_COLORS,
        );
        let _ = ReleaseDC(None, hdc);

        if scan_lines != abs_height as i32 {
            tracing::debug!(
                "get_clipboard_image: GetDIBits copied {} of {} lines",
                scan_lines,
                abs_height
            );
            return None;
        }

        if let Some(rgba) =
            convert_bgra32_to_rgba(&buffer, width as usize, abs_height as usize, is_top_down)
        {
            if let Some(png) = encode_png(&rgba, width as u32, abs_height) {
                tracing::debug!(
                    "get_clipboard_image: converted CF_BITMAP to PNG ({} bytes)",
                    png.len()
                );
                let thumbnail = generate_thumbnail(&png, 64, 64);
                return Some((png, thumbnail));
            }
        }

        None
    }
}

unsafe extern "system" fn window_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_CLIPBOARDUPDATE => {
            let ctx_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *const (
                Arc<RwLock<Option<ClipboardCallback>>>,
                Arc<AtomicBool>,
            );

            if !ctx_ptr.is_null() {
                let (callback, _) = &*ctx_ptr;

                // Get current clipboard item (text or image)
                if let Some(item) = WindowsClipboardMonitor::get_current_item() {
                    if let Some(cb) = callback.read().as_ref() {
                        cb(item);
                    }
                }
            }

            LRESULT(0)
        }
        WM_DESTROY => {
            let ctx_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut (
                Arc<RwLock<Option<ClipboardCallback>>>,
                Arc<AtomicBool>,
            );

            if !ctx_ptr.is_null() {
                drop(Box::from_raw(ctx_ptr));
            }

            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}
