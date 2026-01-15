use std::sync::atomic::{AtomicBool, AtomicIsize, Ordering};
use std::sync::Arc;
use std::thread;

use parking_lot::RwLock;
use windows::Win32::Foundation::{HGLOBAL, HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::System::DataExchange::*;
use windows::Win32::System::Memory::*;
use windows::Win32::System::Ole::{CF_BITMAP, CF_DIB, CF_UNICODETEXT};
use windows::Win32::UI::WindowsAndMessaging::*;

use clipboard_core::domain::{ClipboardItem, ContentType};
use clipboard_core::error::{ClipboardError, Result};
use clipboard_core::ports::{ClipboardCallback, ClipboardMonitor};

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

            // Try CF_DIB first (Device Independent Bitmap)
            let handle = GetClipboardData(CF_DIB.0 as u32);
            if handle.is_err() {
                tracing::debug!("get_clipboard_image: GetClipboardData(CF_DIB) failed");
                let _ = CloseClipboard();
                return None;
            }

            let handle = handle.unwrap();
            if handle.0.is_null() {
                tracing::debug!("get_clipboard_image: handle is null");
                let _ = CloseClipboard();
                return None;
            }

            let hglobal = HGLOBAL(handle.0);
            let ptr = GlobalLock(hglobal);
            if ptr.is_null() {
                tracing::debug!("get_clipboard_image: GlobalLock failed");
                let _ = CloseClipboard();
                return None;
            }

            let size = GlobalSize(hglobal);
            if size == 0 {
                tracing::debug!("get_clipboard_image: GlobalSize returned 0");
                let _ = GlobalUnlock(hglobal);
                let _ = CloseClipboard();
                return None;
            }

            tracing::debug!("get_clipboard_image: DIB data size = {} bytes", size);

            // Read DIB data
            let dib_data = std::slice::from_raw_parts(ptr as *const u8, size);

            // Convert DIB to PNG using the image crate
            let png_data = dib_to_png(dib_data);

            let _ = GlobalUnlock(hglobal);
            let _ = CloseClipboard();

            if let Some(png) = png_data {
                tracing::debug!("get_clipboard_image: PNG conversion succeeded, {} bytes", png.len());
                // Generate thumbnail
                let thumbnail = generate_thumbnail(&png, 64, 64);
                Some((png, thumbnail))
            } else {
                tracing::debug!("get_clipboard_image: dib_to_png failed");
                None
            }
        }
    }

    fn has_image_format() -> bool {
        unsafe {
            // IsClipboardFormatAvailable doesn't require OpenClipboard
            let has_dib = IsClipboardFormatAvailable(CF_DIB.0 as u32).is_ok();
            let has_bitmap = IsClipboardFormatAvailable(CF_BITMAP.0 as u32).is_ok();
            has_dib || has_bitmap
        }
    }

    fn has_text_format() -> bool {
        unsafe {
            // IsClipboardFormatAvailable doesn't require OpenClipboard
            IsClipboardFormatAvailable(CF_UNICODETEXT.0 as u32).is_ok()
        }
    }

    /// Get current clipboard item, preferring images over text
    pub fn get_current_item() -> Option<ClipboardItem> {
        // Check for image first (higher priority)
        let has_image = Self::has_image_format();
        let has_text = Self::has_text_format();
        tracing::debug!("Clipboard formats - has_image: {}, has_text: {}", has_image, has_text);

        if has_image {
            if let Some((png_data, thumbnail)) = Self::get_clipboard_image() {
                tracing::info!("Captured image: {} bytes, thumbnail: {:?}", png_data.len(), thumbnail.as_ref().map(|t| t.len()));
                return Some(ClipboardItem::new_image(png_data, thumbnail));
            } else {
                tracing::warn!("has_image_format() returned true but get_clipboard_image() failed");
            }
        }

        // Fall back to text
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
                    cbSize: std::mem::size_of::<WNDCLASSEXW>() as u32,
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
            _ => Err(ClipboardError::ClipboardAccess(
                format!("Content type {:?} not supported for clipboard", item.content_type)
            )),
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
