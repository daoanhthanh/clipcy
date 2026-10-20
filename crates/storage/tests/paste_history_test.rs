use std::sync::{
    Arc,
    atomic::{AtomicUsize, Ordering},
};

use clipboard_core::{
    application::ClipboardService,
    domain::{ClipboardItem, Settings},
    error::{ClipboardError, Result},
    ports::{ClipboardCallback, ClipboardMonitor, ClipboardRepository, PasteHandler},
};
use clipboard_storage::SqliteClipboardRepository;
use tempfile::tempdir;

struct TestMonitor {
    fail_write: bool,
}

impl ClipboardMonitor for TestMonitor {
    fn start(&self) -> Result<()> {
        Ok(())
    }
    fn stop(&self) -> Result<()> {
        Ok(())
    }
    fn is_running(&self) -> bool {
        false
    }
    fn on_change(&self, _: ClipboardCallback) {}
    fn get_current(&self) -> Result<Option<ClipboardItem>> {
        Ok(None)
    }
    fn set_content(&self, _: &ClipboardItem) -> Result<()> {
        if self.fail_write {
            Err(ClipboardError::ClipboardAccess("Test failure".into()))
        } else {
            Ok(())
        }
    }
    fn set_respect_clipboard_exclusion(&self, _: bool) {}
    fn sample_sequence(&self) -> Result<u32> {
        Ok(1)
    }
    fn apply_capture_policy(&self, _: bool, _: u64, _: Option<u32>) -> Result<()> {
        Ok(())
    }
}

struct TestPasteHandler {
    fail_paste: bool,
}

impl PasteHandler for TestPasteHandler {
    fn simulate_paste(&self) -> Result<()> {
        if self.fail_paste {
            Err(ClipboardError::PasteSimulation("Test failure".into()))
        } else {
            Ok(())
        }
    }
    fn simulate_paste_delayed(&self, _: u64) -> Result<()> {
        self.simulate_paste()
    }
}

#[tokio::test]
async fn test_paste_updates_history_only_after_success() {
    for (fail_write, fail_paste) in [(false, false), (true, false), (false, true)] {
        let dir = tempdir().unwrap();
        let db_path = dir.path().join("test.db");
        let repo = Arc::new(
            SqliteClipboardRepository::new(db_path.clone())
                .await
                .unwrap(),
        );
        let mut older = ClipboardItem::new_text("Older item");
        older.created_at -= chrono::Duration::days(2);
        older.last_used_at = older.created_at;
        let mut newer = ClipboardItem::new_text("Newer item");
        newer.created_at -= chrono::Duration::days(1);
        newer.last_used_at = newer.created_at;
        repo.save(&older).await.unwrap();
        repo.save(&newer).await.unwrap();

        let service = ClipboardService::new(
            Arc::new(TestMonitor { fail_write }),
            repo.clone(),
            Arc::new(TestPasteHandler { fail_paste }),
            Settings::default(),
            tokio::runtime::Handle::current(),
        );
        let notifications = Arc::new(AtomicUsize::new(0));
        let callback_notifications = notifications.clone();
        service.set_on_change(move || {
            callback_notifications.fetch_add(1, Ordering::SeqCst);
        });

        let result = service.paste_item(older.id).await;
        let success = !fail_write && !fail_paste;
        assert_eq!(result.is_ok(), success);
        assert_eq!(notifications.load(Ordering::SeqCst), usize::from(success));
        let items = repo.get_all_metadata(10).await.unwrap();
        assert_eq!(items[0].id, if success { older.id } else { newer.id });
        let stored = repo.get_by_id(older.id).await.unwrap().unwrap();
        assert_eq!(stored.created_at, older.created_at);
        assert_eq!(stored.data, older.data);
        if !success {
            assert_eq!(stored.last_used_at, older.last_used_at);
        }
        let reopened = SqliteClipboardRepository::new(db_path).await.unwrap();
        assert_eq!(
            reopened.get_all_metadata(10).await.unwrap()[0].id,
            items[0].id
        );
    }
}
