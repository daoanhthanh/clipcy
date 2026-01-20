# Clipcy

A fast, modern clipboard manager built with a hybrid architecture: Rust backend + native UI.

## Architecture

Clipcy uses a "Logic Sandwich" architecture:
- **Rust Backend**: Core clipboard logic, SQLite storage, system hooks
- **Native UI**: Platform-specific native UI (WinUI 3 on Windows, SwiftUI on macOS)
- **UniFFI Bridge**: Connects Rust to native languages (C#, Swift)

```mermaid
graph TD
    subgraph Rust Core
        Bridge[UniFFI Bridge]
        Core[Clipboard Core]
        Storage[SQLite Storage]
        Platform[Platform Handlers]
    end

    subgraph Windows
        WinUI[WinUI 3 App]
        CS[C# Bindings]
    end

    subgraph macOS
        SwiftUI[SwiftUI App]
        Swift[Swift Bindings]
    end

    WinUI --> CS --> Bridge
    SwiftUI --> Swift --> Bridge
    Bridge --> Core --> Storage
    Bridge --> Platform
```

## Features

- Clipboard history with text, images, and files support
- Global hotkey (default: `Ctrl+Shift+V`)
- System tray integration with icon and context menu
- SQLite storage
- Native settings UI
- Light/Dark/System theme support
- Auto-start on boot
- Context menu-style popup with acrylic/mica backdrop (Windows 11)
- Borderless floating window (always on top, no taskbar entry)
- Multi-monitor support (appears on cursor's monitor)
- Keyboard-first navigation:
  - First item auto-selected on open
  - Up/Down arrows to navigate
  - Enter to paste selected item
  - Escape to close
  - Auto-focus search on typing
- Auto-hide on click outside or focus loss
- Screen boundary detection
- Preview panel on hover/focus:
  - Larger preview of clipboard content (text, images, files)
  - Intelligent positioning (adapts to available space)
  - Content-specific metadata (dimensions, file size, character/word count)
  - Auto-dismisses when focus moves away
  - Shows copy time, source application, and pin status

## Platform Support

| Platform | UI Technology | Status |
|----------|--------------|--------|
| Windows | WinUI 3 (C#) | ✅ Implemented |
| macOS | SwiftUI | 🔜 Planned |
| Linux | GTK4 | 🔜 Planned |

---

## Windows

### Requirements

#### Development
- Windows 10 version 1809+ (build 17763)
- [Rust](https://rustup.rs/) 1.75+
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 with "Windows App SDK" workload

#### Runtime
- Windows 10 version 1809+ or Windows 11
- Windows App SDK Runtime (bundled)

### Quick Start

```powershell
# Clone the repository
git clone https://github.com/user/clipcy.git
cd clipcy

# Build Rust library
cargo build --release -p clipcy-bridge

# Build WinUI 3 application
cd apps/clipcy-windows-ui
dotnet build

# Run
dotnet run
```

### Building

#### Using Build Script
```powershell
# Debug build
.\scripts\build-windows-ui.ps1

# Release build
.\scripts\build-windows-ui.ps1 -Release

# Clean build
.\scripts\build-windows-ui.ps1 -CleanBuild
```

#### Manual Build

1. **Build Rust Bridge (cdylib)**
   ```bash
   cargo build --release -p clipcy-bridge
   # Output: target/release/clipcy_core.dll
   ```

2. **Generate C# Bindings** (optional - placeholder included)
   ```bash
   # Requires uniffi-bindgen-cs
   uniffi-bindgen-cs --library target/release/clipcy_core.dll --out-dir apps/clipcy-windows-ui/Bindings
   ```

3. **Build WinUI 3 App**
   ```bash
   cd apps/clipcy-windows-ui
   dotnet build -c Release
   ```

### Project Structure

```
clipcy/
├── apps/
│   ├── clipcy-windows-ui/    # WinUI 3 Windows app (C#)
│   ├── clipcy-macos/         # macOS app (placeholder)
│   └── clipcy-linux/         # Linux app (placeholder)
├── crates/
│   ├── bridge/               # UniFFI bridge for native UI
│   ├── core/                 # Domain logic and ports
│   ├── storage/              # SQLite repository
│   ├── platform-windows/     # Windows platform handlers
│   └── settings-common/      # Shared settings types
└── scripts/
    └── build-windows-ui.ps1  # Windows build script
```

### Configuration

Settings are stored in `%LOCALAPPDATA%\clipcy\settings.json`:

```json
{
  "max_history_items": 100,
  "hotkey": "Ctrl+Shift+V",
  "theme": "system",
  "start_on_boot": true,
  "show_notifications": false,
  "show_preview_panel": true,
  "excluded_apps": []
}
```

Database: `%LOCALAPPDATA%\clipcy\clipcy.db`

### Usage

1. Run `Clipcy.exe`
2. Icon appears in system tray
3. Press `Ctrl+Shift+V` to open clipboard history
4. Click an item to paste it
5. Right-click tray icon for menu (Settings, Clear History, Exit)

### Troubleshooting

#### Hotkey not working
- Check if another app is using the same hotkey
- Modify hotkey in Settings

#### Tray icon not visible
- Click the `^` arrow in Windows taskbar to see hidden icons
- Drag the icon to the taskbar

#### App crashes on startup
- Ensure .NET 8 Runtime is installed
- Check logs in `%LOCALAPPDATA%\clipcy\logs\`

---

## macOS

*Coming soon - SwiftUI implementation planned*

### Planned Features
- Menu bar app with `NSStatusItem`
- `MenuBarExtra` for macOS 13+
- Floating `NSPanel` for popup

---

## Linux

*Coming soon - GTK4 implementation planned*

---

## Development

### Hexagonal Architecture

The project follows Hexagonal (Ports & Adapters) Architecture:

- **Core**: Domain models, ports (traits), application services
- **Adapters**: Platform-specific implementations, storage (SQLite)
- **Bridge**: UniFFI FFI layer for native UI binding
- **Apps**: Platform-specific native UI applications

### Adding a New Platform

1. Create platform handlers in `crates/platform-{os}/`
2. Implement core traits (ClipboardMonitor, HotkeyHandler, etc.)
3. Build the bridge: `cargo build -p clipcy-bridge`
4. Generate bindings for your language (Swift, Kotlin, etc.)
5. Create native UI app in `apps/clipcy-{os}/`

---

## License

MIT
