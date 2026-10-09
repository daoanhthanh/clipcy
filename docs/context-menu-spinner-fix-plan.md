# Context Menu Spinner: Fix Plans

## Problem Summary
- The cursor shows a busy/spinning state when opening the Clipcy popup (clipboard history window).
- The UI thread performs synchronous, CPU- and I/O-heavy work during popup activation, so Windows marks the app as busy.

## Root Causes
1) **Synchronous thumbnail decoding on UI thread**
   - Each list item binds to `ThumbnailImage`, and the getter synchronously decodes PNG bytes into a `BitmapImage` via `SetSource`.
   - This happens during list population, so a history with many images blocks the UI thread.

2) **Synchronous full-content fetch during preview mapping**
   - `MapToPreviewData` immediately calls `App.Bridge.GetItemData(...)` to load full item data on the UI thread.
   - This bridges into Rust and blocks on SQLite calls (`runtime.block_on(...)`), which can stall the UI.

## Fix Plans (Algorithms)

### Plan A (Best: Minimum CPU/I/O on popup show)
**Goal:** Make popup show non-blocking by deferring expensive work and doing it asynchronously.

**Algorithm (Thumbnail decode):**
1. Replace `ThumbnailImage` getter with a lightweight placeholder (null or cached result).
2. When an item is realized (container created), enqueue a background task to decode the thumbnail.
3. When decoding completes, marshal back to UI thread and update the item (or a per-item image cache) and raise property change.

**Algorithm (Full content fetch):**
1. In `MapToPreviewData`, remove synchronous `GetItemData(...)` call.
2. Only load full data when preview is shown, using `LoadFullData` async callback.
3. For text/rtf/files, update preview contents asynchronously after the popup is already visible.

**Why it’s minimal resource:**
- Work is lazy and only runs for visible items.
- No blocking on UI thread.
- Avoids decoding thumbnails for items never shown.

### Plan B (Limit workload by virtualization + throttling)
**Goal:** Reduce per-show workload by limiting how much content is hydrated.

**Algorithm:**
1. Enable/confirm UI virtualization for the ListView (if not already active by default).
2. Cap items rendered on initial show (e.g., load first N, then append as idle).
3. Throttle refresh: do not re-query full history on every activation if already up-to-date.

**Why it’s minimal resource:**
- Lowers initial UI work and spreads it over time.
- Reduces repeated database queries.

### Plan C (Cache at bridge boundary)
**Goal:** Reuse decoded thumbnails and preview data across popup shows.

**Algorithm:**
1. Add a thumbnail cache (LRU) keyed by item id or content hash.
2. Cache decoded `BitmapImage` or raw bytes in memory with a size budget.
3. On show, first check cache; decode only on miss.

**Why it’s minimal resource:**
- Avoids repeated decoding and disk reads.
- Keeps memory bounded with LRU eviction.

## Recommended Best Path (Minimum Resource Use)
**Plan A + light caching**
- Primary win: move thumbnail decode and full data fetch off UI thread and make it lazy.
- Optional small LRU cache for thumbnails to avoid repeated decode with bounded memory.

## Concrete Areas to Change
- `apps/clipcy-windows/Services/RustBridgeService.cs`: `ClipboardItemViewModel.ThumbnailImage` (remove sync decoding in getter).
- `apps/clipcy-windows/MainWindow.xaml.cs`: `MapToPreviewData` (remove sync `GetItemData`, use async `LoadFullData`).
- Optionally add a small cache in `MainViewModel` or a dedicated image service.

## Validation
- Open popup with large history including images.
- Verify no busy cursor and UI is responsive.
- Confirm thumbnails and previews appear asynchronously after popup is visible.
