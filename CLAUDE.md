# CLAUDE.md - Clipcy Project Guide

## Core Principles

- Never use emojis.

## Commit Authorship

When committing code changes:
- Never add Claude as a commit author.
- Always commit as using the default git settings

## Documentation Style

When creating or updating markdown documentation files:
- **Never create .md files unless explicitly instructed.**
- **Be extremely concise** - engineers scan, they don't read novels
- **Only include essential information** - what they need to know, not what's possible to explain
- **Prefer examples over prose** - show the pattern, not the theory
- **Assume technical competence** - skip obvious explanations
- **Front-load critical info** - put warnings and key concepts first
- **Delete verbose explanations** - if it takes more than 3 sentences, it's probably too long

Default to 1-2 sentence explanations. Only expand when complexity absolutely requires it.

## Project Overview

Clipcy is a cross-platform clipboard manager written in Rust. It monitors clipboard changes, stores history in SQLite,
and provides a native UI for quick access to clipboard history via global hotkeys.

## Architecture

The project follows **Hexagonal Architecture** (Ports and Adapters):

```
┌─────────────────────────────────────────────────────────────┐
│                      Applications                           │
│       (clipcy-windows, clipcy-macos, clipcy-linux)          │
└─────────────────────────────────────────────────────────────┘
                              │
┌───────────────────────────────────────────────────────────────────────────┐
│                           Platform Adapters                               │
│  (clipcy-platform-windows, clipcy-platform-macos, clipcy-platform-linux)  │
└───────────────────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────┐
│                      Core Domain                            │
│           (ports/traits, domain models, services)           │
└─────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────┐
│                    Storage Adapter                          │
│                  (SQLite via sqlx)                          │
└─────────────────────────────────────────────────────────────┘
```

## Directory Structure

```
clipcy/
├── Cargo.toml              # Workspace configuration
├── crates/
│   ├── core/               # Domain logic and port definitions
│   │   └── src/
│   │       ├── domain/     # ClipboardItem, Settings, Theme
│   │       ├── ports/      # Trait definitions (interfaces)
│   │       ├── application/# ClipboardService, AppController
│   │       └── error/      # Error types
│   │
│   ├── storage/            # SQLite repository implementation
│   │   └── src/
│   │       └── sqlite_repository.rs
│   │
│   ├── platform-windows/   # Windows-specific implementations
│   │   └── src/
│   │       ├── clipboard.rs  # WindowsClipboardMonitor
│   │       ├── hotkey.rs     # WindowsHotkeyHandler
│   │       ├── paste.rs      # WindowsPasteHandler
│   │       ├── theme.rs      # WindowsThemeDetector
│   │       └── ui/menu.rs    # WindowsUIHandler
│   │
│   ├── platform-macos/     # macOS stubs (not implemented)
│   └── platform-linux/     # Linux stubs (not implemented)
│
└── apps/
    ├── clipcy-windows/  # Windows application entry point
    ├── clipcy-macos/    # macOS application entry point
    └── clipcy-linux/    # Linux application entry point
```

## Key Components

### Core Crate (`crates/core`)

**Domain Models:**

- `ClipboardItem` - Represents a clipboard entry (text, image, files)
- `ContentType` - Enum for content types (Text, Image, Files, Html, Rtf)
- `Settings` - Application settings (max history, hotkey, theme)
- `Theme` - Light/Dark/System theme enum

**Ports (Traits):**

- `ClipboardMonitor` - Monitor clipboard changes, get/set content
- `ClipboardRepository` - Persist clipboard items (save, get, search, prune)
- `HotkeyHandler` - Register/unregister global hotkeys
- `PasteHandler` - Simulate paste action
- `ThemeDetector` - Detect system theme changes
- `UIHandler` - Show/hide popup menu, handle UI events

**Application Services:**

- `ClipboardService` - Orchestrates clipboard monitoring and storage
- `AppController` - Main application controller

### Storage Crate (`crates/storage`)

- `SqliteClipboardRepository` - Async SQLite implementation using sqlx
- Database stored at: `%LOCALAPPDATA%/clipcy/clipcy.db`

### Platform-Windows Crate (`crates/platform-windows`)

- Uses `windows` crate v0.62.2
- Hidden message window for clipboard monitoring (`WM_CLIPBOARDUPDATE`)
- Global hotkey registration via `RegisterHotKey`
- Native popup menu via `TrackPopupMenu`
- Theme detection via WinRT `UISettings`

## Build Commands

```bash
# Build all crates
cargo build --workspace

# Build release
cargo build --workspace --release

# Build specific platform
cargo build -p clipcy-windows

# Run tests
cargo test --workspace

# Run the Windows application
cargo run -p clipcy-windows
```

## Important Patterns

### Send/Sync Safety for Windows Handles

Windows handles (`HWND`) contain raw pointers that are not `Send`/`Sync`. We use `AtomicIsize` to store handles safely:

```rust
// Store HWND as isize to avoid Send/Sync issues
hwnd: Arc<AtomicIsize>,

// Convert back when needed
let hwnd = HWND(hwnd_val as * mut std::ffi::c_void);
```

The structs also implement `unsafe impl Send` and `unsafe impl Sync` manually.

### Windows 0.62.2 API Patterns

```rust
// CreateWindowExW returns Result
let hwnd = match CreateWindowExW(...) {
Ok(h) => h,
Err(e) => { /* handle error */ }
};

// Many functions take Option<HWND> or Option<HANDLE>
RegisterHotKey(Some(hwnd), ...);
SetClipboardData(format, Some(handle));

// Boolean parameters use bool, not BOOL
InsertMenuItemW(menu, index, true, & item);
```

### Async Repository

The storage layer uses async sqlx:

```rust
pub async fn new(path: PathBuf) -> Result<Self>
pub async fn save(&self, item: &ClipboardItem) -> Result<()>
pub async fn get_all(&self, limit: usize) -> Result<Vec<ClipboardItem>>
```

### Debouncing

Clipboard changes are debounced (300ms) to avoid duplicate entries from rapid clipboard updates.

## Dependencies

| Dependency       | Purpose                         |
|------------------|---------------------------------|
| `windows` 0.62.2 | Windows API bindings            |
| `sqlx` 0.8       | Async SQLite                    |
| `tokio`          | Async runtime                   |
| `parking_lot`    | Fast synchronization primitives |
| `uuid`           | Unique IDs for clipboard items  |
| `chrono`         | Timestamps                      |
| `serde`          | Serialization                   |
| `tracing`        | Logging                         |

## Testing

Unit tests are in `crates/core/src/domain/clipboard_item.rs`.

Integration tests for the repository are in `crates/storage/tests/repository_test.rs`.

```bash
# Run all tests
cargo test --workspace

# Run specific test
cargo test test_save_and_retrieve
```

## Default Hotkey

`Ctrl+Shift+V` - Opens the clipboard history popup

## Notes for Contributors

1. Always use the port traits from `clipboard_core::ports` - don't depend on platform implementations directly
2. Platform-specific code goes in `platform-*` crates
3. The core crate should have no platform-specific dependencies
4. Use `tracing` for logging, not `println!`
5. Handle Windows API errors gracefully - many functions return `Result`
6. Test on Windows before committing platform-windows changes


## Core Principles

- Never use emojis.

## Commit Authorship

When committing code changes:
- Never add Claude as a commit author.
- Always commit as using the default git settings

## Documentation Style

When creating or updating markdown documentation files:
- **Never create .md files unless explicitly instructed.**
- **Be extremely concise** - engineers scan, they don't read novels
- **Only include essential information** - what they need to know, not what's possible to explain
- **Prefer examples over prose** - show the pattern, not the theory
- **Assume technical competence** - skip obvious explanations
- **Front-load critical info** - put warnings and key concepts first
- **Delete verbose explanations** - if it takes more than 3 sentences, it's probably too long

Default to 1-2 sentence explanations. Only expand when complexity absolutely requires it.
