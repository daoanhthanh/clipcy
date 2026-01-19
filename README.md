# Clipcy

A fast, modern clipboard manager for Windows built with Rust.

## Features

- Clipboard history with text, images, and files support
- Global hotkey (default: `Ctrl+Shift+V`)
- System tray integration
- SQLite storage
- Modern settings UI (Tauri + Solid.js)
- Light/Dark/System theme support
- Auto-start on boot

## Requirements

### Development

- [Rust](https://rustup.rs/) 1.75+
- [Bun](https://bun.sh/) (for settings UI)
- Windows 10/11

### Runtime

- Windows 10/11
- WebView2 Runtime (pre-installed on Windows 11, [download for Windows 10](https://developer.microsoft.com/en-us/microsoft-edge/webview2/))

## Quick Start

```bash
# Clone the repository
git clone https://github.com/user/clipcy.git
cd clipcy

# Run in development mode
cargo run -p clipcy-windows
```

## Development

### Project Structure

```
clipcy/
├── apps/
│   ├── clipcy-windows/       # Main Windows application
│   └── clipcy-settings/      # Settings window (Tauri + Solid.js)
│       └── ui/               # Solid.js frontend
├── crates/
│   ├── core/                 # Domain logic and port definitions
│   ├── storage/              # SQLite repository
│   ├── platform-windows/     # Windows-specific implementations
│   └── settings-common/      # Shared settings types
└── dist/                     # Build output
```

### Running in Development

```bash
# Main app (with console logging)
cargo run -p clipcy-windows

# Build settings UI (required once before running settings)
cd apps/clipcy-settings/ui
bun install
bun run build
cd ../../..

# Run settings app standalone
cargo run -p clipcy-settings
```

### Architecture

The project follows Hexagonal Architecture:

- **Core**: Domain models, ports (traits), application services
- **Adapters**: Platform-specific implementations (Windows), storage (SQLite)
- **Apps**: Entry points that wire everything together

## Building

### Release Build

```bash
# Build all
cargo build --workspace --release

# Build main app only
cargo build -p clipcy-windows --release

# Build settings app
cd apps/clipcy-settings/ui && bun run build && cd ../../..
cargo build -p clipcy-settings --release
```

### Create Portable Package

```bash
# Build everything
cargo build -p clipcy-windows --release
cd apps/clipcy-settings/ui && bun run build && cd ../../..
cargo build -p clipcy-settings --release

# Create dist folder
mkdir -p dist/clipcy-portable
cp target/release/clipcy.exe dist/clipcy-portable/
cp target/release/clipcy-settings.exe dist/clipcy-portable/

# Create ZIP (PowerShell)
cd dist
powershell -Command "Compress-Archive -Path 'clipcy-portable/*' -DestinationPath 'clipcy-portable.zip' -Force"
```

Output: `dist/clipcy-portable.zip` (~6 MB)

## Configuration

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

## Usage

1. Run `clipcy.exe`
2. Icon appears in system tray
3. Press `Ctrl+Shift+V` to open clipboard history
4. Right-click tray icon for menu (Settings, Exit)

## Troubleshooting

### App doesn't start

Check `%LOCALAPPDATA%\clipcy\settings.json` - ensure `hotkey` is not empty:
```json
"hotkey": "Ctrl+Shift+V"
```

### Tray icon not visible

Click the `^` arrow in Windows taskbar to see hidden icons.

### Settings window is blank

Ensure WebView2 Runtime is installed.

## License

MIT
