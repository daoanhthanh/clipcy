//! Exercises the real Windows exclusion format and live settings without deleting history.
//! Writes the interactive clipboard and restores previous plain text when available.

#![cfg(target_os = "windows")]

use std::mem::size_of;
use std::sync::Arc;
use std::time::Duration;

use clipboard_core::{
    application::ClipboardService,
    domain::{ClipboardItem, ContentType, Settings},
    error::Result,
    ports::{ClipboardMonitor, ClipboardRepository, PasteHandler},
};
use clipboard_storage::SqliteClipboardRepository;
use clipcy_platform_windows::WindowsClipboardMonitor;
use tempfile::tempdir;
use windows::Win32::Foundation::{GlobalFree, HANDLE, HWND};
use windows::Win32::System::DataExchange::{
    CloseClipboard, EmptyClipboard, IsClipboardFormatAvailable, OpenClipboard,
    RegisterClipboardFormatW, SetClipboardData,
};
use windows::Win32::System::Memory::{GMEM_MOVEABLE, GlobalAlloc, GlobalLock, GlobalUnlock};
use windows::Win32::System::Ole::CF_UNICODETEXT;
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DestroyWindow, HWND_MESSAGE, WINDOW_EX_STYLE, WS_POPUP,
};
use windows::core::w;

struct NoPaste;

impl PasteHandler for NoPaste {
    fn simulate_paste(&self) -> Result<()> {
        Ok(())
    }
    fn simulate_paste_delayed(&self, _: u64) -> Result<()> {
        Ok(())
    }
}

struct RestoreClipboard {
    monitor: Arc<WindowsClipboardMonitor>,
    previous: Option<ClipboardItem>,
}

impl Drop for RestoreClipboard {
    fn drop(&mut self) {
        let _ = self.monitor.stop();
        if let Some(item) = self.previous.as_ref() {
            let _ = self.monitor.set_content(item);
        }
    }
}

struct ClipboardWriter(HWND);

impl ClipboardWriter {
    fn open() -> windows::core::Result<Self> {
        // A real owner window is needed for EmptyClipboard/SetClipboardData.
        let hwnd = unsafe {
            CreateWindowExW(
                WINDOW_EX_STYLE::default(),
                w!("STATIC"),
                w!("ClipcyClipboardExclusionSmoke"),
                WS_POPUP,
                0,
                0,
                0,
                0,
                Some(HWND_MESSAGE),
                None,
                None,
                None,
            )?
        };
        for attempt in 0..5 {
            if unsafe { OpenClipboard(Some(hwnd)) }.is_ok() {
                let writer = Self(hwnd);
                unsafe { EmptyClipboard()? };
                return Ok(writer);
            }
            if attempt < 4 {
                std::thread::sleep(Duration::from_millis(50));
            }
        }
        let error = windows::core::Error::from_thread();
        let _ = unsafe { DestroyWindow(hwnd) };
        Err(error)
    }

    fn set(&self, format: u32, bytes: &[u8]) -> windows::core::Result<()> {
        unsafe {
            let memory = GlobalAlloc(GMEM_MOVEABLE, bytes.len())?;
            let target = GlobalLock(memory);
            if target.is_null() {
                let error = windows::core::Error::from_thread();
                let _ = GlobalFree(Some(memory));
                return Err(error);
            }
            std::ptr::copy_nonoverlapping(bytes.as_ptr(), target.cast::<u8>(), bytes.len());
            let _ = GlobalUnlock(memory);
            if let Err(error) = SetClipboardData(format, Some(HANDLE(memory.0))) {
                let _ = GlobalFree(Some(memory));
                return Err(error);
            }
            Ok(())
        }
    }
}

impl Drop for ClipboardWriter {
    fn drop(&mut self) {
        let _ = unsafe { CloseClipboard() };
        let _ = unsafe { DestroyWindow(self.0) };
    }
}

fn copy_text(text: &str, marker: Option<[u8; 4]>) -> windows::core::Result<()> {
    let writer = ClipboardWriter::open()?;
    if let Some(payload) = marker {
        let format = unsafe {
            RegisterClipboardFormatW(w!("ExcludeClipboardContentFromMonitorProcessing"))
        };
        assert_ne!(format, 0, "could not register the exclusion clipboard format");
        writer.set(format, &payload)?;
        assert!(unsafe { IsClipboardFormatAvailable(format) }.is_ok());
    }
    let wide: Vec<u16> = text.encode_utf16().chain(std::iter::once(0)).collect();
    let bytes = unsafe {
        std::slice::from_raw_parts(wide.as_ptr().cast::<u8>(), wide.len() * size_of::<u16>())
    };
    writer.set(CF_UNICODETEXT.0 as u32, bytes)
}

async fn settle(service: &ClipboardService) {
    tokio::time::sleep(Duration::from_millis(600)).await;
    service.flush_pending().await.unwrap();
}

#[tokio::test]
#[ignore = "writes the interactive Windows clipboard; run explicitly without other clipboard tests"]
async fn native_exclusion_presence_obeys_live_setting_and_preserves_history() {
    let monitor = Arc::new(WindowsClipboardMonitor::new());
    let _restore = RestoreClipboard {
        previous: monitor
            .get_current()
            .unwrap()
            .filter(|item| item.content_type == ContentType::Text),
        monitor: monitor.clone(),
    };
    let dir = tempdir().unwrap();
    let repo = Arc::new(
        SqliteClipboardRepository::new(dir.path().join("clipboard-exclusion-smoke.db"))
            .await
            .unwrap(),
    );
    let service = ClipboardService::new(
        monitor.clone(),
        repo.clone(),
        Arc::new(NoPaste),
        Settings::default(),
        tokio::runtime::Handle::current(),
    );
    service.start_monitoring().unwrap();
    tokio::time::sleep(Duration::from_millis(300)).await;

    // Default-off behavior captures even a marked copy with arbitrary nonzero data.
    copy_text("CLIPCYEXCLUSIONBEFORE", Some([0x89, 0xab, 0xcd, 0xef])).unwrap();
    settle(&service).await;
    let previous = repo.search("CLIPCYEXCLUSIONBEFORE", 10).await.unwrap();
    assert_eq!(previous.len(), 1, "default-off setting suppressed marked content");
    let previous = previous.into_iter().next().unwrap();
    repo.set_pinned(previous.id, true).await.unwrap();

    let mut settings = service.get_settings();
    settings.respect_clipboard_exclusion = true;
    service.update_settings(settings.clone());
    let retained = repo.get_by_id(previous.id).await.unwrap().unwrap();
    assert_eq!(retained.data, previous.data, "enabling exclusion changed old data");
    assert!(retained.pinned, "enabling exclusion changed an existing pin");

    copy_text("CLIPCYEXCLUSIONBLOCKED", Some([0x89, 0xab, 0xcd, 0xef])).unwrap();
    assert!(monitor.get_current().unwrap().is_none());
    settle(&service).await;
    assert!(repo.search("CLIPCYEXCLUSIONBLOCKED", 10).await.unwrap().is_empty());

    // Zero bytes still request exclusion: the format's presence alone matters.
    copy_text("CLIPCYEXCLUSIONZERO", Some([0; 4])).unwrap();
    settle(&service).await;
    assert!(repo.search("CLIPCYEXCLUSIONZERO", 10).await.unwrap().is_empty());

    copy_text("CLIPCYEXCLUSIONORDINARY", None).unwrap();
    settle(&service).await;
    let ordinary = repo.search("CLIPCYEXCLUSIONORDINARY", 10).await.unwrap();
    assert_eq!(ordinary.len(), 1, "ordinary copy was suppressed with exclusion enabled");
    assert_eq!(ordinary[0].data, b"CLIPCYEXCLUSIONORDINARY");

    // Applying the setting must not unpause capture or import the paused value.
    service.set_capture_paused(true).await.unwrap();
    settings.respect_clipboard_exclusion = false;
    service.update_settings(settings);
    assert!(service.is_capture_paused());
    copy_text("CLIPCYEXCLUSIONPAUSED", Some([0x89, 0xab, 0xcd, 0xef])).unwrap();
    settle(&service).await;
    assert!(repo.search("CLIPCYEXCLUSIONPAUSED", 10).await.unwrap().is_empty());
    service.set_capture_paused(false).await.unwrap();
    settle(&service).await;
    assert!(repo.search("CLIPCYEXCLUSIONPAUSED", 10).await.unwrap().is_empty());

    copy_text("CLIPCYEXCLUSIONAFTER", Some([0x89, 0xab, 0xcd, 0xef])).unwrap();
    settle(&service).await;
    let captured = repo.search("CLIPCYEXCLUSIONAFTER", 10).await.unwrap();
    assert_eq!(captured.len(), 1, "live disable did not capture the next marked copy");
    assert_eq!(captured[0].data, b"CLIPCYEXCLUSIONAFTER");
    assert!(repo.get_by_id(previous.id).await.unwrap().unwrap().pinned);
    service.stop_monitoring().unwrap();
}
