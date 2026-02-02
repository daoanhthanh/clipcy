use clipboard_core::domain::ClipboardItem;
use clipboard_core::error::{ClipboardError, Result};
use std::collections::VecDeque;
use tokio::runtime::Handle;
use tokio::task;
use tracing::{debug, warn};

use windows::Win32::Foundation::RPC_E_CHANGED_MODE;
use windows::Win32::System::Com::{COINIT_APARTMENTTHREADED, CoInitializeEx, CoUninitialize};

use chrono::{DateTime, Utc};
use windows::ApplicationModel::DataTransfer::{
    Clipboard, ClipboardHistoryItem, ClipboardHistoryItemsResultStatus, DataPackageView, StandardDataFormats
};
use windows::Graphics::Imaging::{BitmapDecoder, BitmapEncoder};
use windows::Storage::Streams::{
    DataReader, InMemoryRandomAccessStream, RandomAccessStreamReference,
};

const WINDOWS_TO_UNIX_EPOCH_100NS: i64 = 116_444_736_000_000_000;

/// Import clipboard history items from Windows. Falls back to an empty list on failure.
pub async fn import_system_clipboard_history() -> Result<VecDeque<ClipboardItem>> {
    match task::spawn_blocking(|| import_history_sta()).await {
        Ok(Ok(items)) => {
            if !items.is_empty() {
                tracing::info!(
                    "Successfully imported {} clipboard history items",
                    items.len()
                );
            } else {
                tracing::debug!("No clipboard history items imported");
            }
            Ok(items)
        }
        Ok(Err(e)) => {
            warn!("Failed to import system clipboard history: {}", e);
            Ok(VecDeque::new())
        }
        Err(e) => {
            warn!("Failed to join clipboard history task: {}", e);
            Ok(VecDeque::new())
        }
    }
}

fn import_history_sta() -> Result<VecDeque<ClipboardItem>> {
    let _com = ComStaGuard::new()?;
    let handle = Handle::current();
    handle.block_on(import_history_internal())
}

struct ComStaGuard;

impl ComStaGuard {
    fn new() -> Result<Self> {
        unsafe {
            let hr = CoInitializeEx(None, COINIT_APARTMENTTHREADED);
            if hr.is_err() {
                if hr == RPC_E_CHANGED_MODE {
                    return Err(ClipboardError::ClipboardAccess(
                        "Clipboard history requires STA thread (got MTA)".to_string(),
                    ));
                }
                return Err(ClipboardError::ClipboardAccess(format!(
                    "Failed to initialize COM STA: 0x{:08X}",
                    hr.0
                )));
            }
        }
        Ok(Self)
    }
}

impl Drop for ComStaGuard {
    fn drop(&mut self) {
        unsafe {
            CoUninitialize();
        }
    }
}

async fn import_history_internal() -> Result<VecDeque<ClipboardItem>> {
    // Ensure clipboard history is enabled by the user/OS
    let is_enabled = Clipboard::IsHistoryEnabled().map_err(|e| {
        ClipboardError::ClipboardAccess(format!("Failed to check history support: {}", e))
    })?;

    if !is_enabled {
        debug!("Clipboard history is disabled");
        return Ok(VecDeque::new());
    }

    let result = Clipboard::GetHistoryItemsAsync()
        .map_err(|e| {
            ClipboardError::ClipboardAccess(format!("Failed to request history items: {}", e))
        })?
        .await
        .map_err(|e| {
            ClipboardError::ClipboardAccess(format!("Failed to await history items: {}", e))
        })?;

    let status = result.Status().map_err(|e| {
        ClipboardError::ClipboardAccess(format!("Failed to read history status: {}", e))
    })?;

    if status != ClipboardHistoryItemsResultStatus::Success {
        return Err(ClipboardError::ClipboardAccess(format!(
            "Clipboard history unavailable (status: {:?})",
            status
        )));
    }

    let items_view = result.Items().map_err(|e| {
        ClipboardError::ClipboardAccess(format!("Failed to read history items: {}", e))
    })?;

    let mut collected: Vec<ClipboardItem> = Vec::new();

    // Items are ordered newest-first; iterate backwards to insert oldest-first for stable history order
    let count = items_view.Size().map_err(|e| {
        ClipboardError::ClipboardAccess(format!("Failed to read history size: {}", e))
    })?;

    for idx in (0..count).rev() {
        match items_view.GetAt(idx) {
            Ok(item) => match convert_history_item(item).await {
                Ok(Some(converted)) => collected.push(converted),
                Ok(None) => {
                    debug!("Skipping unsupported clipboard history item");
                }
                Err(e) => {
                    warn!("Failed to convert clipboard history item: {}", e);
                }
            },
            Err(e) => {
                warn!("Failed to read clipboard history entry: {}", e);
            }
        }
    }

    Ok(collected.into())
}

fn map_win_error(context: &str, err: windows::core::Error) -> ClipboardError {
    ClipboardError::ClipboardAccess(format!("{}: {}", context, err))
}

fn apply_metadata(item: &mut ClipboardItem, timestamp: DateTime<Utc>, source_app: Option<String>) {
    item.created_at = timestamp;
    item.source_app = source_app;
}

fn convert_timestamp(dt: windows::Foundation::DateTime) -> DateTime<Utc> {
    let unix_100ns = dt.UniversalTime - WINDOWS_TO_UNIX_EPOCH_100NS;
    let secs = unix_100ns / 10_000_000;
    let nanos = ((unix_100ns % 10_000_000) * 100) as u32;

    let fallback = DateTime::<Utc>::from_timestamp(0, 0).expect("valid zero timestamp");
    DateTime::<Utc>::from_timestamp(secs, nanos).unwrap_or(fallback)
}

async fn convert_history_item(history_item: ClipboardHistoryItem) -> Result<Option<ClipboardItem>> {
    let timestamp = history_item
        .Timestamp()
        .map(convert_timestamp)
        .map_err(|e| map_win_error("Failed to read history timestamp", e))?;

    // Source application is not exposed by the current Windows SDK; leave empty
    let source_app: Option<String> = None;

    let content = history_item
        .Content()
        .map_err(|e| map_win_error("Failed to read history content", e))?;

    let has_files = content
        .Contains(
            &StandardDataFormats::StorageItems()
                .map_err(|e| map_win_error("Failed to get StorageItems format", e))?,
        )
        .map_err(|e| map_win_error("Failed to detect storage items", e))?;
    let has_bitmap = content
        .Contains(
            &StandardDataFormats::Bitmap()
                .map_err(|e| map_win_error("Failed to get Bitmap format", e))?,
        )
        .map_err(|e| map_win_error("Failed to detect bitmap", e))?;
    let has_html = content
        .Contains(
            &StandardDataFormats::Html()
                .map_err(|e| map_win_error("Failed to get HTML format", e))?,
        )
        .map_err(|e| map_win_error("Failed to detect HTML", e))?;
    let has_rtf = content
        .Contains(
            &StandardDataFormats::Rtf()
                .map_err(|e| map_win_error("Failed to get RTF format", e))?,
        )
        .map_err(|e| map_win_error("Failed to detect RTF", e))?;
    let has_text = content
        .Contains(
            &StandardDataFormats::Text()
                .map_err(|e| map_win_error("Failed to get Text format", e))?,
        )
        .map_err(|e| map_win_error("Failed to detect text", e))?;

    if has_files {
        if let Some(paths) = get_storage_paths(&content).await? {
            if !(has_bitmap && should_prefer_image(&paths)) {
                let mut item = ClipboardItem::new_files(paths);
                apply_metadata(&mut item, timestamp, source_app);
                return Ok(Some(item));
            }
        }
    }

    let html_content = if has_html {
        get_html(&content).await?
    } else {
        None
    };

    if let Some(ref html) = html_content {
        if looks_like_table_html(html) {
            let mut item = ClipboardItem::new_html(html.clone());
            apply_metadata(&mut item, timestamp, source_app);
            return Ok(Some(item));
        }
    }

    let text_content = if has_text {
        get_text(&content).await?
    } else {
        None
    };

    if let Some(ref text) = text_content {
        if looks_like_tabular_text(text) {
            let mut item = ClipboardItem::new_text(text.clone());
            apply_metadata(&mut item, timestamp, source_app);
            return Ok(Some(item));
        }
    }

    if has_bitmap {
        if let Some((png_data, thumbnail)) = get_bitmap_png(&content).await? {
            let mut item = ClipboardItem::new_image(png_data, thumbnail);
            apply_metadata(&mut item, timestamp, source_app);
            return Ok(Some(item));
        }
    }

    if let Some(html) = html_content {
        if text_content.is_none() {
            let mut item = ClipboardItem::new_html(html);
            apply_metadata(&mut item, timestamp, source_app);
            return Ok(Some(item));
        }
    }

    if has_rtf {
        if let Some(rtf) = get_rtf(&content).await? {
            let mut item = ClipboardItem::new_rtf(rtf);
            apply_metadata(&mut item, timestamp, source_app);
            return Ok(Some(item));
        }
    }

    if let Some(text) = text_content {
        let mut item = ClipboardItem::new_text(text);
        apply_metadata(&mut item, timestamp, source_app);
        return Ok(Some(item));
    }

    Ok(None)
}

async fn get_storage_paths(view: &DataPackageView) -> Result<Option<Vec<String>>> {
    let items = view
        .GetStorageItemsAsync()
        .map_err(|e| map_win_error("Failed to request storage items", e))?
        .await
        .map_err(|e| map_win_error("Failed to fetch storage items", e))?;

    let size = items
        .Size()
        .map_err(|e| map_win_error("Failed to read storage size", e))?;

    let mut paths = Vec::with_capacity(size as usize);
    for idx in 0..size {
        let item = items
            .GetAt(idx)
            .map_err(|e| map_win_error("Failed to read storage item", e))?;
        let path = item
            .Path()
            .map_err(|e| map_win_error("Failed to read storage path", e))?;
        let path_str = path.to_string();
        if !path_str.is_empty() {
            paths.push(path_str);
        }
    }

    if paths.is_empty() {
        Ok(None)
    } else {
        Ok(Some(paths))
    }
}

fn should_prefer_image(paths: &[String]) -> bool {
    if paths.len() != 1 {
        return false;
    }

    let lower = paths[0].to_lowercase();
    let is_image = lower.ends_with(".png")
        || lower.ends_with(".jpg")
        || lower.ends_with(".jpeg")
        || lower.ends_with(".bmp")
        || lower.ends_with(".gif");

    let is_temp_location = lower.contains("\\appdata\\local\\")
        || lower.contains("\\temp\\")
        || lower.contains("\\tmp\\")
        || lower.contains("screenclip");

    is_image && is_temp_location
}

async fn get_bitmap_png(view: &DataPackageView) -> Result<Option<(Vec<u8>, Option<Vec<u8>>)>> {
    let reference = view
        .GetBitmapAsync()
        .map_err(|e| map_win_error("Failed to request bitmap", e))?
        .await
        .map_err(|e| map_win_error("Failed to fetch bitmap", e))?;

    let png_data = read_stream_as_png(reference).await?;
    let thumbnail = generate_thumbnail(&png_data, 64, 64);
    Ok(Some((png_data, thumbnail)))
}

async fn read_stream_as_png(reference: RandomAccessStreamReference) -> Result<Vec<u8>> {
    let stream = reference
        .OpenReadAsync()
        .map_err(|e| map_win_error("Failed to open bitmap stream", e))?
        .await
        .map_err(|e| map_win_error("Failed to read bitmap stream", e))?;

    let decoder = BitmapDecoder::CreateAsync(&stream)
        .map_err(|e| map_win_error("Failed to create bitmap decoder", e))?
        .await
        .map_err(|e| map_win_error("Failed to init bitmap decoder", e))?;

    let software_bitmap = decoder
        .GetSoftwareBitmapAsync()
        .map_err(|e| map_win_error("Failed to decode bitmap", e))?
        .await
        .map_err(|e| map_win_error("Failed to decode bitmap", e))?;

    let out_stream = InMemoryRandomAccessStream::new()
        .map_err(|e| map_win_error("Failed to allocate memory stream", e))?;

    let encoder = BitmapEncoder::CreateAsync(
        BitmapEncoder::PngEncoderId()
            .map_err(|e| map_win_error("Failed to get PNG encoder id", e))?,
        &out_stream,
    )
    .map_err(|e| map_win_error("Failed to create PNG encoder", e))?
    .await
    .map_err(|e| map_win_error("Failed to init PNG encoder", e))?;

    encoder
        .SetSoftwareBitmap(&software_bitmap)
        .map_err(|e| map_win_error("Failed to set bitmap for encoding", e))?;

    encoder
        .FlushAsync()
        .map_err(|e| map_win_error("Failed to flush PNG encoder", e))?
        .await
        .map_err(|e| map_win_error("Failed to flush PNG encoder", e))?;

    out_stream
        .Seek(0)
        .map_err(|e| map_win_error("Failed to rewind PNG stream", e))?;

    let size = out_stream
        .Size()
        .map_err(|e| map_win_error("Failed to read PNG size", e))?
        .min(u32::MAX as u64);

    let input = out_stream
        .GetInputStreamAt(0)
        .map_err(|e| map_win_error("Failed to get PNG input stream", e))?;

    let reader = DataReader::CreateDataReader(&input)
        .map_err(|e| map_win_error("Failed to create data reader", e))?;

    reader
        .LoadAsync(size as u32)
        .map_err(|e| map_win_error("Failed to load PNG data", e))?
        .await
        .map_err(|e| map_win_error("Failed to load PNG data", e))?;

    let mut buffer = vec![0u8; size as usize];
    reader
        .ReadBytes(&mut buffer)
        .map_err(|e| map_win_error("Failed to read PNG bytes", e))?;

    reader
        .Close()
        .map_err(|e| map_win_error("Failed to close data reader", e))?;

    Ok(buffer)
}

async fn get_html(view: &DataPackageView) -> Result<Option<String>> {
    let raw = view
        .GetHtmlFormatAsync()
        .map_err(|e| map_win_error("Failed to request HTML", e))?
        .await
        .map_err(|e| map_win_error("Failed to fetch HTML", e))?;

    if raw.is_empty() {
        return Ok(None);
    }

    Ok(Some(raw.to_string()))
}

async fn get_rtf(view: &DataPackageView) -> Result<Option<String>> {
    let rtf = view
        .GetRtfAsync()
        .map_err(|e| map_win_error("Failed to request RTF", e))?
        .await
        .map_err(|e| map_win_error("Failed to fetch RTF", e))?;

    if rtf.is_empty() {
        Ok(None)
    } else {
        Ok(Some(rtf.to_string()))
    }
}

async fn get_text(view: &DataPackageView) -> Result<Option<String>> {
    let text = view
        .GetTextAsync()
        .map_err(|e| map_win_error("Failed to request text", e))?
        .await
        .map_err(|e| map_win_error("Failed to fetch text", e))?;

    if text.is_empty() {
        Ok(None)
    } else {
        Ok(Some(text.to_string()))
    }
}

fn looks_like_table_html(html: &str) -> bool {
    let lower = html.to_ascii_lowercase();
    lower.contains("<table") || (lower.contains("<tr") && lower.contains("<td"))
}

fn looks_like_tabular_text(text: &str) -> bool {
    if !text.contains('\t') {
        return false;
    }

    if text.contains('\n') {
        return true;
    }

    text.matches('\t').count() >= 2
}

fn generate_thumbnail(png_data: &[u8], max_width: u32, max_height: u32) -> Option<Vec<u8>> {
    use image::ImageEncoder;
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

    if encoder
        .write_image(
            thumbnail.as_bytes(),
            thumbnail.width(),
            thumbnail.height(),
            image::ColorType::Rgba8,
        )
        .is_ok()
    {
        Some(png_bytes)
    } else {
        None
    }
}
