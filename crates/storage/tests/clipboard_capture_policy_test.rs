use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU32, AtomicU64, Ordering};
use std::time::Duration;

use async_trait::async_trait;
use clipboard_core::{
    application::ClipboardService,
    domain::{ClipboardItem, Settings},
    error::{ClipboardError, Result},
    ports::{ClipboardCallback, ClipboardMonitor, ClipboardObservation, ClipboardRepository, PasteHandler},
};
use clipboard_storage::SqliteClipboardRepository;
use parking_lot::Mutex;
use tempfile::tempdir;
use tokio::sync::Notify;
use uuid::Uuid;

struct GateMonitor {
    callback: Mutex<Option<ClipboardCallback>>,
    paused: AtomicBool,
    epoch: AtomicU64,
    baseline: AtomicU32,
    sequence: AtomicU32,
    fail_sequence: AtomicBool,
}

impl GateMonitor {
    fn new() -> Self {
        Self {
            callback: Mutex::new(None),
            paused: AtomicBool::new(false),
            epoch: AtomicU64::new(1),
            baseline: AtomicU32::new(0),
            sequence: AtomicU32::new(10),
            fail_sequence: AtomicBool::new(false),
        }
    }

    fn emit(&self, text: &str, sequence: u32) {
        if self.paused.load(Ordering::SeqCst) {
            return;
        }
        let baseline = self.baseline.load(Ordering::SeqCst);
        if baseline != 0 && (sequence == 0 || sequence == baseline) {
            return;
        }
        self.emit_raw(text, sequence, self.epoch.load(Ordering::SeqCst));
    }

    fn emit_raw(&self, text: &str, sequence: u32, epoch: u64) {
        let callback = self.callback.lock();
        if let Some(callback) = callback.as_ref() {
            callback(ClipboardObservation {
                epoch,
                sequence,
                item: ClipboardItem::new_text(text),
            });
        }
    }
}

impl ClipboardMonitor for GateMonitor {
    fn start(&self) -> Result<()> {
        Ok(())
    }
    fn stop(&self) -> Result<()> {
        Ok(())
    }
    fn is_running(&self) -> bool {
        true
    }
    fn on_change(&self, callback: ClipboardCallback) {
        *self.callback.lock() = Some(callback);
    }
    fn get_current(&self) -> Result<Option<ClipboardItem>> {
        Ok(None)
    }
    fn set_content(&self, _: &ClipboardItem) -> Result<()> {
        Ok(())
    }
    fn sample_sequence(&self) -> Result<u32> {
        if self.fail_sequence.load(Ordering::SeqCst) {
            return Err(ClipboardError::ClipboardAccess("sequence unavailable".into()));
        }
        let sequence = self.sequence.load(Ordering::SeqCst);
        if sequence == 0 {
            Err(ClipboardError::ClipboardAccess("sequence unavailable".into()))
        } else {
            Ok(sequence)
        }
    }
    fn apply_capture_policy(
        &self,
        paused: bool,
        epoch: u64,
        resume_baseline: Option<u32>,
    ) -> Result<()> {
        if paused {
            self.paused.store(true, Ordering::SeqCst);
            self.epoch.store(epoch, Ordering::SeqCst);
            self.baseline.store(0, Ordering::SeqCst);
        } else {
            self.epoch.store(epoch, Ordering::SeqCst);
            self.baseline
                .store(resume_baseline.unwrap_or(0), Ordering::SeqCst);
            self.paused.store(false, Ordering::SeqCst);
        }
        Ok(())
    }
}

struct NoPaste;

impl PasteHandler for NoPaste {
    fn simulate_paste(&self) -> Result<()> {
        Ok(())
    }
    fn simulate_paste_delayed(&self, _: u64) -> Result<()> {
        Ok(())
    }
}

struct GatedRepo {
    inner: SqliteClipboardRepository,
    gate: AtomicBool,
    entered: Notify,
    release: Notify,
}

impl GatedRepo {
    fn new(inner: SqliteClipboardRepository) -> Self {
        Self {
            inner,
            gate: AtomicBool::new(false),
            entered: Notify::new(),
            release: Notify::new(),
        }
    }
}

#[async_trait]
impl ClipboardRepository for GatedRepo {
    async fn save(&self, item: &ClipboardItem) -> Result<()> {
        if self.gate.load(Ordering::SeqCst) {
            let waiting = self.release.notified();
            self.entered.notify_waiters();
            waiting.await;
        }
        self.inner.save(item).await
    }
    async fn get_by_id(&self, id: Uuid) -> Result<Option<ClipboardItem>> {
        self.inner.get_by_id(id).await
    }
    async fn get_all(&self, limit: usize) -> Result<Vec<ClipboardItem>> {
        self.inner.get_all(limit).await
    }
    async fn touch_last_used_at(&self, id: Uuid) -> Result<()> {
        self.inner.touch_last_used_at(id).await
    }
    async fn delete(&self, id: Uuid) -> Result<()> {
        self.inner.delete(id).await
    }
    async fn clear(&self) -> Result<()> {
        self.inner.clear().await
    }
    async fn search(&self, query: &str, limit: usize) -> Result<Vec<ClipboardItem>> {
        self.inner.search(query, limit).await
    }
    async fn count(&self) -> Result<usize> {
        self.inner.count().await
    }
    async fn prune(&self, max_items: usize) -> Result<usize> {
        self.inner.prune(max_items).await
    }
    async fn set_pinned(&self, id: Uuid, pinned: bool) -> Result<()> {
        self.inner.set_pinned(id, pinned).await
    }
}

async fn harness() -> (ClipboardService, Arc<GateMonitor>, Arc<GatedRepo>) {
    let dir = tempdir().unwrap();
    let repo = SqliteClipboardRepository::new(dir.path().join("clipcy.db"))
        .await
        .unwrap();
    let repo = Arc::new(GatedRepo::new(repo));
    let monitor = Arc::new(GateMonitor::new());
    let service = ClipboardService::new(
        monitor.clone(),
        repo.clone(),
        Arc::new(NoPaste),
        Settings::default(),
        tokio::runtime::Handle::current(),
    );
    service.start_monitoring().unwrap();
    (service, monitor, repo)
}

async fn contains(repo: &GatedRepo, text: &str) -> bool {
    repo.search(text, 20)
        .await
        .unwrap()
        .iter()
        .any(|item| item.preview == text)
}

#[tokio::test]
async fn pause_drops_pending_copy_and_does_not_resurrect_it() {
    let (service, monitor, repo) = harness().await;
    monitor.emit("CLIPCY-PAUSE-PENDING", 11);
    service.set_capture_paused(true).await.unwrap();
    assert!(service.is_capture_paused());

    tokio::time::sleep(Duration::from_millis(400)).await;
    service.flush_pending().await.unwrap();
    assert!(!contains(&repo, "CLIPCY-PAUSE-PENDING").await);

    monitor.emit("CLIPCY-PAUSE-WHILE-PAUSED", 12);
    tokio::time::sleep(Duration::from_millis(400)).await;
    service.flush_pending().await.unwrap();
    assert!(!contains(&repo, "CLIPCY-PAUSE-WHILE-PAUSED").await);
}

#[tokio::test]
async fn pause_lets_in_flight_save_finish_and_drops_the_queued_copy() {
    let (service, monitor, repo) = harness().await;
    repo.gate.store(true, Ordering::SeqCst);
    let entered = repo.entered.notified();
    tokio::pin!(entered);

    monitor.emit("CLIPCY-PAUSE-INFLIGHT", 11);
    let flush = service.flush_pending();
    tokio::pin!(flush);
    tokio::select! {
        _ = &mut entered => {}
        _ = &mut flush => panic!("flush finished before the gated save"),
        _ = tokio::time::sleep(Duration::from_secs(2)) => panic!("gated save did not start"),
    }

    monitor.emit("CLIPCY-PAUSE-QUEUED", 12);
    let pause = service.set_capture_paused(true);
    tokio::pin!(pause);
    repo.release.notify_one();
    pause.await.unwrap();
    flush.await.unwrap();

    assert!(contains(&repo, "CLIPCY-PAUSE-INFLIGHT").await);
    tokio::time::sleep(Duration::from_millis(400)).await;
    service.flush_pending().await.unwrap();
    assert!(!contains(&repo, "CLIPCY-PAUSE-QUEUED").await);
}

#[tokio::test]
async fn resume_does_not_import_paused_sequence_and_stores_the_next_copy_once() {
    let (service, monitor, repo) = harness().await;
    monitor.emit("CLIPCY-PAUSE-KEEP", 11);
    tokio::time::sleep(Duration::from_millis(400)).await;
    assert!(contains(&repo, "CLIPCY-PAUSE-KEEP").await);

    service.set_capture_paused(true).await.unwrap();
    monitor.sequence.store(20, Ordering::SeqCst);
    monitor.emit("CLIPCY-PAUSE-HIDDEN", 20);
    service.set_capture_paused(false).await.unwrap();
    assert!(!service.is_capture_paused());

    monitor.emit_raw("CLIPCY-PAUSE-BASELINE", 20, 1);
    monitor.emit("CLIPCY-PAUSE-BASELINE", 20);
    tokio::time::sleep(Duration::from_millis(400)).await;
    service.flush_pending().await.unwrap();
    assert!(!contains(&repo, "CLIPCY-PAUSE-HIDDEN").await);
    assert!(!contains(&repo, "CLIPCY-PAUSE-BASELINE").await);
    assert!(contains(&repo, "CLIPCY-PAUSE-KEEP").await);

    monitor.sequence.store(21, Ordering::SeqCst);
    monitor.emit("CLIPCY-PAUSE-FRESH", 21);
    monitor.emit("CLIPCY-PAUSE-FRESH", 21);
    tokio::time::sleep(Duration::from_millis(400)).await;
    let matches = repo
        .search("CLIPCY-PAUSE-FRESH", 20)
        .await
        .unwrap()
        .into_iter()
        .filter(|item| item.preview == "CLIPCY-PAUSE-FRESH")
        .count();
    assert_eq!(matches, 1);
}

#[tokio::test]
async fn unavailable_sequence_keeps_pause_and_rejects_late_old_epoch() {
    let (service, monitor, repo) = harness().await;
    service.set_capture_paused(true).await.unwrap();
    monitor.fail_sequence.store(true, Ordering::SeqCst);
    let error = service.set_capture_paused(false).await.unwrap_err();
    assert!(error.to_string().contains("remains paused"));
    assert!(service.is_capture_paused());

    monitor.fail_sequence.store(false, Ordering::SeqCst);
    monitor.sequence.store(30, Ordering::SeqCst);
    service.set_capture_paused(false).await.unwrap();
    monitor.emit_raw("CLIPCY-PAUSE-STALE", 31, 1);
    tokio::time::sleep(Duration::from_millis(400)).await;
    service.flush_pending().await.unwrap();
    assert!(!contains(&repo, "CLIPCY-PAUSE-STALE").await);
}

#[tokio::test]
async fn restart_clears_pause_and_captures_again() {
    let (service, monitor, repo) = harness().await;
    service.set_capture_paused(true).await.unwrap();
    service.stop_monitoring().unwrap();
    assert!(!service.is_capture_paused());

    service.start_monitoring().unwrap();
    assert!(!service.is_capture_paused());
    monitor.emit("CLIPCY-PAUSE-RESTART", 40);
    tokio::time::sleep(Duration::from_millis(400)).await;
    assert!(contains(&repo, "CLIPCY-PAUSE-RESTART").await);
}
