//! Proves the real Windows listener honors pause, resume baseline, and a later copy.
//! Restores the previous Unicode clipboard text when that read succeeds.

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

struct NoPaste;

impl PasteHandler for NoPaste {
    fn simulate_paste(&self) -> Result<()> {
        Ok(())
    }
    fn simulate_paste_delayed(&self, _: u64) -> Result<()> {
        Ok(())
    }
}

fn restore_clipboard(previous: Option<String>, monitor: &WindowsClipboardMonitor) {
    let Some(text) = previous else {
        return;
    };
    let item = ClipboardItem::new_text(text);
    let _ = monitor.set_content(&item);
}

#[tokio::test]
#[ignore = "writes the interactive Windows clipboard; run explicitly"]
async fn native_pause_does_not_capture_until_a_fresh_copy_after_resume() {
    let monitor = Arc::new(WindowsClipboardMonitor::new());
    let previous = monitor
        .get_current()
        .ok()
        .flatten()
        .filter(|item| item.content_type == ContentType::Text)
        .map(|item| String::from_utf8_lossy(&item.data).into_owned());

    let dir = tempdir().unwrap();
    let repo = Arc::new(
        SqliteClipboardRepository::new(dir.path().join("pause-smoke.db"))
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
    // Let the hidden listener window register before mutating the clipboard.
    tokio::time::sleep(Duration::from_millis(300)).await;

    let result = async {
        service.set_capture_paused(true).await?;
        monitor.set_content(&ClipboardItem::new_text("CLIPCY-PAUSE-PROBE-A"))?;
        tokio::time::sleep(Duration::from_millis(500)).await;
        service.flush_pending().await?;
        let paused_hits = repo.search("CLIPCY-PAUSE-PROBE-A", 10).await?;
        assert!(
            paused_hits.is_empty(),
            "paused copy was stored: {}",
            paused_hits.len()
        );

        service.set_capture_paused(false).await?;
        tokio::time::sleep(Duration::from_millis(500)).await;
        service.flush_pending().await?;
        let resumed_hits = repo.search("CLIPCY-PAUSE-PROBE-A", 10).await?;
        assert!(
            resumed_hits.is_empty(),
            "resume imported the paused clipboard value"
        );

        monitor.set_content(&ClipboardItem::new_text("CLIPCY-PAUSE-PROBE-B"))?;
        tokio::time::sleep(Duration::from_millis(500)).await;
        service.flush_pending().await?;
        let fresh_hits = repo.search("CLIPCY-PAUSE-PROBE-B", 10).await?;
        assert_eq!(fresh_hits.len(), 1, "fresh copy after resume was not stored once");
        Ok::<(), clipboard_core::error::ClipboardError>(())
    }
    .await;

    service.stop_monitoring().unwrap();
    restore_clipboard(previous, &monitor);
    result.unwrap();
}
