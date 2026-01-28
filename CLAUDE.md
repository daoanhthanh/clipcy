# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

### Windows Development (Primary Platform)

**Quick Build:**
```powershell
# Debug build
.\scripts\build-windows-ui.ps1

# Release build
.\scripts\build-windows-ui.ps1 -Release

# Clean build with binding regeneration
.\scripts\build-windows-ui.ps1 -CleanBuild -GenerateBindings
```

**Build Installer:**
```powershell
.\scripts\build-windows-installer.ps1
# Requires Inno Setup 6 (ISCC.exe on PATH)
# Outputs to dist/windows/win-x64 and dist/installer
```

**Manual Build Steps:**
```bash
# 1. Build Rust bridge library (cdylib)
cargo build --release -p clipcy-bridge
# Output: target/release/clipcy_core.dll

# 2. Generate C# bindings (requires uniffi-bindgen-cs v0.10.0+v0.29.4)
uniffi-bindgen-cs --library target/release/clipcy_core.dll --out-dir apps/clipcy-windows-ui/Bindings

# 3. Build/run WinUI 3 app
cd apps/clipcy-windows-ui
dotnet run                    # Debug
dotnet build -c Release       # Release
```

**Install uniffi-bindgen-cs:**
```bash
cargo install uniffi-bindgen-cs --git https://github.com/NordSecurity/uniffi-bindgen-cs --tag v0.10.0+v0.29.4
```

### Rust Workspace

```bash
# Build all crates
cargo build --workspace
cargo build --workspace --release

# Run all tests
cargo test --workspace

# Run specific test by name
cargo test test_save_and_retrieve
```

## Architecture Overview

Clipcy uses a "Logic Sandwich" architecture with **Hexagonal (Ports & Adapters)** pattern:

```
┌─────────────────────────────────────┐
│  Native UI (WinUI 3 C#)             │
│  - MainWindow, SettingsWindow       │
│  - ViewModels (MVVM)                │
│  - Services (RustBridgeService)     │
└──────────────┬──────────────────────┘
               │ UniFFI Bindings (C#)
┌──────────────▼──────────────────────┐
│  Bridge (crates/bridge)             │
│  - ClipboardEngine                  │
│  - DTOs (ClipboardItemDto, etc)     │
│  - BridgeEvent, UiCallback          │
└──────────────┬──────────────────────┘
               │ UniFFI (.udl)
┌──────────────▼──────────────────────┐
│  Core (crates/core)                 │
│  - domain: Models                   │
│  - ports: Trait definitions         │
│  - application: Services            │
└──────────────┬──────────────────────┘
               │ Implemented by adapters
┌──────────────▼──────────────────────┐
│  Adapters                           │
│  - storage: SQLite repository       │
│  - platform-windows: Win handlers   │
└─────────────────────────────────────┘
```

### Key Layers

**1. Core Domain (`crates/core/`)**
- Pure business logic, no dependencies on UI or platform
- `domain/`: ClipboardItem, ContentType, Settings, Theme
- `ports/`: Trait definitions (ClipboardRepository, ClipboardMonitor, HotkeyHandler, PasteHandler, ThemeDetector, UIHandler)
- `application/`: ClipboardService, AppController

**2. Adapters**
- `crates/storage/`: SQLite implementation
  - `AsyncInitRepository`: Non-blocking initialization wrapper
  - `SqliteClipboardRepository`: Main repository
- `crates/platform-windows/`: Windows-specific implementations
  - WindowsClipboardMonitor, WindowsHotkeyHandler, WindowsPasteHandler, WindowsThemeDetector, WindowsAutostartHandler
- `crates/platform-macos/`, `crates/platform-linux/`: Placeholders (planned)

**3. Bridge Layer (`crates/bridge/`)**
- UniFFI-based FFI boundary connecting Rust ↔ C#
- `ClipboardEngine`: Main API exposed to native UI
- DTOs for cross-language data transfer
- `BridgeEvent` enum: Events sent to UI (HistoryUpdated, HotkeyTriggered, ThemeChanged, ClipboardChanged, etc.)
- `UiCallback` trait: Implemented by native UI to receive events

**4. Native UI (`apps/clipcy-windows-ui/`)**
- WinUI 3 C# application (MVVM pattern)
- `Services/RustBridgeService.cs`: Wraps Rust ClipboardEngine
- `ViewModels/`: MainViewModel, ClipboardItemViewModel
- `Services/`: TrayService, PreviewService, WindowService, ThemeManager

### Critical Patterns

**Async Initialization:**
- `AsyncInitRepository` initializes SQLite database on background thread
- UI shows immediately while backend initializes
- `BridgeEvent::Ready` sent when initialization complete

**Event Flow:**
- Rust → C#: ClipboardEngine sends `BridgeEvent` via `UiCallback.on_event()`
- C# → Rust: UI calls methods on ClipboardEngine (get_history, paste_item, etc.)
- Background thread handles I/O, events marshaled to UI thread

**Settings Management:**
- `CachedSettingsManager` keeps settings in memory
- File: `%LOCALAPPDATA%\clipcy\settings.json`
- Changes applied immediately via `ClipboardEngine::save_settings()`

## Development Workflows

### Modifying Rust Backend

1. Edit code in `crates/core/`, `crates/storage/`, or `crates/platform-windows/`
2. Rebuild bridge: `cargo build --release -p clipcy-bridge`
3. If UniFFI interface changed (`crates/bridge/src/clipcy_core.udl`), regenerate bindings:
   ```bash
   uniffi-bindgen-cs --library target/release/clipcy_core.dll --out-dir apps/clipcy-windows-ui/Bindings
   ```
4. Rebuild C# app: `cd apps/clipcy-windows-ui && dotnet build`

### Modifying Windows UI Only

1. Edit files in `apps/clipcy-windows-ui/`
2. Run: `dotnet run` (no Rust rebuild needed)

### Adding New Content Type

1. Add variant to `ContentType` in `crates/core/src/domain/clipboard_item.rs`
2. Update `ContentTypeDto` mapping in `crates/bridge/src/lib.rs`
3. Handle in `crates/storage/src/sqlite_repository.rs`
4. Update UI rendering in `apps/clipcy-windows-ui/Services/PreviewService.cs`

### Adding New Setting

1. Add field to `Settings` in `crates/settings-common/src/lib.rs`
2. Update `SettingsDto` in `crates/bridge/src/lib.rs`
3. Add UI control in `apps/clipcy-windows-ui/SettingsWindow.xaml`
4. Update `Settings::default()` for default value
5. Apply in `ClipboardEngine::save_settings()` if runtime change needed

### Adding Platform Feature

1. Define trait in `crates/core/src/ports/`
2. Implement in `crates/platform-windows/src/`
3. Wire up in `crates/bridge/src/lib.rs` ClipboardEngine
4. Expose through bridge if UI needs access

## Data Locations

- Settings: `%LOCALAPPDATA%\clipcy\settings.json`
- Database: `%LOCALAPPDATA%\clipcy\clipcy.db` (SQLite with WAL mode)
- Logs: `%LOCALAPPDATA%\clipcy\logs\YYYY-MM-DD.clipcy.log`

## Code Style

**Rust:**
- Use `tracing` for logging, not `println!`
- Keep platform code out of `crates/core`
- Async code uses tokio runtime

**C# (.editorconfig):**
- 4-space indentation, CRLF
- PascalCase for classes/methods
- Max line length: 120 characters

## Common Issues

**Build fails with "cannot find DLL":**
- Ensure `cargo build --release -p clipcy-bridge` succeeded
- Check `target/release/clipcy_core.dll` exists

**C# bindings out of sync:**
- Regenerate after UniFFI interface changes
- Interface defined in `crates/bridge/src/clipcy_core.udl`

**Database locked errors:**
- Only one instance should run
- Check for zombie processes
