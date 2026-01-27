# Clipcy Startup Optimization Plan

## Progress Checkpoints

### Phase 5: Remove 10ms Polling Loop (Quick Win) ✓ COMPLETED
- [x] Analyze current message loop implementation
- [x] Replace PeekMessageW+sleep with GetMessageW
- [x] Update stop() method to post WM_QUIT message
- [x] Test compilation succeeds
- [x] Commit changes
- [ ] Test clipboard monitoring functionality (requires manual testing with running app)
- [ ] Verify CPU usage is 0% when idle (requires manual testing with Task Manager)

### Phase 4: Settings Cache + Background Load ✓ COMPLETED
- [x] Create CachedSettingsManager struct
- [x] Implement load_async() method
- [x] Implement get() for immediate access
- [x] Implement load_now() for synchronous loading
- [x] Add parking_lot dependency
- [x] Update ClipboardEngine to use cached settings
- [x] Update start() to use get() + load_async()
- [x] Update get_settings() to use cache
- [x] Update get_history() to use cached max_history
- [x] Test compilation succeeds
- [x] Commit changes
- [ ] Test with default settings (requires manual testing)
- [ ] Test settings persistence after background load (requires manual testing)

### Phase 3: Async Database Initialization ✓ COMPLETED
- [x] Create AsyncInitRepository wrapper
- [x] Implement start_init_async() background task
- [x] Implement get_repo() with timeout waiting
- [x] Implement ClipboardRepository trait delegation
- [x] Add helper methods (get_all_metadata, search_metadata)
- [x] Add parking_lot dependency to storage crate
- [x] Update ClipboardEngine to use AsyncInitRepository
- [x] Remove blocking block_on() call in new()
- [x] Test compilation succeeds
- [x] Commit changes
- [ ] Test first clipboard save (requires manual testing)
- [ ] Verify DB init completes in background (check logs)
- [ ] Measure init time improvement

### Phase 2: Centralized Windows Message Loop
- [ ] Design WindowsMessageDispatcher interface
- [ ] Implement centralized window creation
- [ ] Migrate clipboard monitoring to dispatcher
- [ ] Migrate hotkey handling to dispatcher
- [ ] Migrate theme detection to dispatcher
- [ ] Remove old separate implementations
- [ ] Test all event types work correctly
- [ ] Commit changes

### Phase 1: Split Initialization
- [ ] Add EngineState enum
- [ ] Refactor ClipboardEngine::new() to minimal init
- [ ] Create spawn_background_init() method
- [ ] Add BridgeEvent for initialization stages
- [ ] Update UI to show tray icon immediately
- [ ] Test background initialization completes
- [ ] Test error handling for failed initialization
- [ ] Measure startup time (<100ms target)
- [ ] Commit changes

### Final Validation
- [ ] Run full test suite
- [ ] Benchmark startup time (target: <100ms to UI)
- [ ] Verify 0% CPU usage when idle
- [ ] Verify all services active in <300ms
- [ ] No regressions in clipboard monitoring
- [ ] No regressions in hotkey handling
- [ ] Settings persist correctly
- [ ] Document performance improvements

---

## Current Problems

**Startup blocking operations:**
1. Database schema initialization (`block_on()` at startup)
2. Settings loading (`block_on()` at startup)
3. Hotkey registration (synchronous)
4. Clipboard monitor spawns immediately (10ms polling loop)

**Estimated startup time: 500-1500ms** (blocking on database I/O)

## PowerToys Patterns to Apply

### Pattern 1: Eager Load + Lazy Init

**Current:** Engine creates ALL components, starts monitoring immediately
**Target:** Initialize UI + critical handlers FIRST, defer heavy operations

```
Current Flow:
start() → DB init → settings load → monitor start → hotkey register → theme setup

Optimized Flow:
start() → minimal state → show UI → background(DB + settings + monitor)
```

### Pattern 2: Centralized Event Dispatch

**Current:** Multiple Windows message loops (clipboard monitor, hotkey handler)
**Target:** Single hidden window + centralized message dispatcher

### Pattern 3: Background Async Operations

**Current:** Everything initialized synchronously in `start()`
**Target:** UI appears immediately, background tasks spawn

### Pattern 4: Cached Settings

**Current:** Settings loaded from disk at startup via `block_on()`
**Target:** Settings loaded in background, cached in memory

## Implementation Plan

### Phase 1: Split Initialization (Critical Path)

**Goal:** UI appears in <50ms, services initialize in background

#### Changes to `ClipboardEngine`

```rust
// New states
enum EngineState {
    Uninitialized,
    Starting,       // UI visible, services loading
    Ready,          // All services active
    Error(String),
}

// Split initialization
impl ClipboardEngine {
    pub fn new() -> Self {
        // ONLY initialize logging + basic state
        // Do NOT create tokio runtime yet
        // Do NOT open database
        // Do NOT load settings
    }

    pub fn start_fast(&self) -> Result<()> {
        // 1. Spawn UI immediately (send ReadyForUI event)
        // 2. Return immediately
        // 3. Spawn background initialization task
        self.spawn_background_init();
    }

    fn spawn_background_init(&self) {
        std::thread::spawn(|| {
            // Now create tokio runtime
            // Open database connection
            // Load settings
            // Start clipboard monitoring
            // Register hotkeys
            // Send FullyInitialized event
        });
    }
}
```

#### Benefits
- UI appears instantly (<50ms)
- User sees tray icon immediately
- Background initialization doesn't block UI thread
- Graceful error handling if DB fails to open

### Phase 2: Centralized Windows Message Loop

**Goal:** One hidden window, not 3+ separate windows

#### New Component: `WindowsMessageDispatcher`

```rust
pub struct WindowsMessageDispatcher {
    hwnd: Arc<AtomicIsize>,
    // Centralized handlers
    clipboard_callbacks: Vec<Box<dyn Fn(ClipboardItem) + Send>>,
    hotkey_callbacks: HashMap<i32, Box<dyn Fn() + Send>>,
    theme_callbacks: Vec<Box<dyn Fn(Theme) + Send>>,
}

impl WindowsMessageDispatcher {
    pub fn new() -> Self {
        // Create single hidden window
        // Register for: WM_CLIPBOARDUPDATE, WM_HOTKEY, WM_SETTINGCHANGE
    }

    pub fn register_clipboard_listener(&mut self, cb: impl Fn(ClipboardItem) + Send + 'static) {
        self.clipboard_callbacks.push(Box::new(cb));
    }

    pub fn register_hotkey(&mut self, id: i32, cb: impl Fn() + Send + 'static) -> Result<()> {
        RegisterHotKey(Some(self.hwnd()), id, ...);
        self.hotkey_callbacks.insert(id, Box::new(cb));
    }

    fn window_proc(&self, msg: u32, wparam: WPARAM, lparam: LPARAM) {
        match msg {
            WM_CLIPBOARDUPDATE => {
                let item = self.get_clipboard_item();
                for cb in &self.clipboard_callbacks {
                    cb(item.clone());
                }
            }
            WM_HOTKEY => {
                if let Some(cb) = self.hotkey_callbacks.get(&(wparam.0 as i32)) {
                    cb();
                }
            }
            WM_SETTINGCHANGE => {
                let theme = self.detect_theme();
                for cb in &self.theme_callbacks {
                    cb(theme);
                }
            }
            _ => {}
        }
    }
}
```

#### Benefits
- Single message loop instead of 3+
- Lower memory footprint (~3 HWNDs → 1 HWND)
- Centralized error handling
- Easier to add new event types

### Phase 3: Lazy Database Connection

**Goal:** Don't open DB until first clipboard change

#### New Pattern: Connection Pool on Demand

```rust
pub struct LazyRepository {
    pool: Arc<Mutex<Option<SqlitePool>>>,
    db_path: PathBuf,
}

impl LazyRepository {
    pub fn new(db_path: PathBuf) -> Self {
        // Just store path, don't connect
        Self {
            pool: Arc::new(Mutex::new(None)),
            db_path,
        }
    }

    async fn ensure_connected(&self) -> Result<SqlitePool> {
        let mut pool_guard = self.pool.lock();
        if let Some(pool) = pool_guard.as_ref() {
            return Ok(pool.clone());
        }

        // Connect now
        let pool = Self::connect_and_init(&self.db_path).await?;
        *pool_guard = Some(pool.clone());
        Ok(pool)
    }

    pub async fn save(&self, item: &ClipboardItem) -> Result<()> {
        let pool = self.ensure_connected().await?;
        // ... save logic
    }
}
```

#### Benefits
- Startup doesn't wait for DB schema creation
- First clipboard change may be slightly slower (~100-200ms)
- But startup is instant

### Phase 4: Settings Cache + Background Load

**Goal:** Use cached settings, load from disk in background

#### New Pattern: Optimistic Settings

```rust
pub struct CachedSettingsManager {
    cache: Arc<RwLock<Settings>>,
    disk_path: PathBuf,
    loaded: Arc<AtomicBool>,
}

impl CachedSettingsManager {
    pub fn new() -> Self {
        // Return with default settings immediately
        Self {
            cache: Arc::new(RwLock::new(Settings::default())),
            disk_path: Self::settings_path(),
            loaded: Arc::new(AtomicBool::new(false)),
        }
    }

    pub fn load_async(&self) {
        let cache = self.cache.clone();
        let path = self.disk_path.clone();
        let loaded = self.loaded.clone();

        tokio::spawn(async move {
            if let Ok(settings) = Self::load_from_disk(&path).await {
                *cache.write() = settings;
                loaded.store(true, Ordering::Release);
            }
        });
    }

    pub fn get(&self) -> Settings {
        // Always returns immediately (defaults or loaded)
        self.cache.read().clone()
    }
}
```

#### Benefits
- Startup doesn't block on settings I/O
- App uses sensible defaults until settings load
- Settings load in <50ms in background

### Phase 5: Remove 10ms Polling Loop

**Goal:** Event-driven clipboard monitoring, not polling

#### Current Problem
```rust
// clipboard.rs:970-980
loop {
    PeekMessageW(&mut msg, None, 0, 0, PM_REMOVE);
    std::thread::sleep(Duration::from_millis(10)); // CPU waste!
    if shutdown.load(Ordering::Acquire) {
        break;
    }
}
```

**Optimization:** Use `GetMessageW()` (blocking) instead of `PeekMessageW()` + sleep

```rust
// Blocking wait for messages - no CPU usage when idle
loop {
    let result = GetMessageW(&mut msg, None, 0, 0);
    if result == BOOL(0) || shutdown.load(Ordering::Acquire) {
        break;
    }
    DispatchMessageW(&msg);
}
```

#### Benefits
- 0% CPU usage when idle (vs ~0.5-1% with polling)
- Instant response to clipboard changes
- No arbitrary 10ms delay

## Expected Performance

### Current Startup
```
0ms:    Process start
500ms:  Database init (blocking)
600ms:  Settings load (blocking)
650ms:  Clipboard monitor spawned
700ms:  Hotkey registered
750ms:  UI appears
```

### Optimized Startup
```
0ms:    Process start
20ms:   Minimal engine init
50ms:   UI appears ✓
        [Background: DB init starts]
150ms:  DB ready
200ms:  Clipboard monitoring active
250ms:  All services ready
```

**Improvement: 750ms → 50ms = 15x faster to UI**

## Implementation Order

1. **Phase 5 first** (quick win, low risk)
   - Replace PeekMessageW+sleep with GetMessageW
   - Test clipboard monitoring still works

2. **Phase 4** (medium complexity)
   - Implement CachedSettingsManager
   - Default settings allow app to run

3. **Phase 3** (medium complexity)
   - Lazy database connection
   - First save triggers connection

4. **Phase 2** (high complexity)
   - Refactor to centralized message dispatcher
   - Requires careful testing of all event types

5. **Phase 1** (high complexity)
   - Split engine initialization
   - Add state machine
   - Background initialization thread

## Testing Strategy

### Performance Benchmarks
```rust
#[test]
fn bench_startup_time() {
    let start = Instant::now();
    let engine = ClipboardEngine::new();
    engine.start_fast().unwrap();
    let ui_ready_time = start.elapsed();

    assert!(ui_ready_time < Duration::from_millis(100));
}
```

### Correctness Tests
- Clipboard monitoring works after background init
- Settings persist correctly with async loading
- Hotkeys registered correctly after UI appears
- No race conditions between UI and background init

## Rollback Plan

Each phase is independent:
- Phase 5: Revert message loop changes
- Phase 4: Revert to synchronous settings load
- Phase 3: Revert to eager DB connection
- Phase 2: Keep separate message loops
- Phase 1: Revert to synchronous initialization

## Risk Assessment

| Phase | Risk | Mitigation |
|-------|------|------------|
| 5 | Low | GetMessageW is standard pattern, well-tested |
| 4 | Low | Default settings always available |
| 3 | Medium | First save may fail if DB path invalid |
| 2 | High | Centralized dispatch harder to debug |
| 1 | High | State machine complexity increases |

## Success Metrics

- [ ] UI appears in <100ms
- [ ] All services active in <300ms
- [ ] 0% idle CPU usage
- [ ] No regressions in clipboard monitoring
- [ ] No regressions in hotkey handling
- [ ] Settings persist correctly

## References

- PowerToys: Eager load + lazy init (STARTUP_ARCHITECTURE.md:164-197)
- PowerToys: Centralized keyboard hook (STARTUP_ARCHITECTURE.md:287-322)
- PowerToys: Async background tasks (STARTUP_ARCHITECTURE.md:374-397)
