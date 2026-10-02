# Clipcy Windows UI

Native WinUI 3 frontend for Clipcy clipboard manager.

## Architecture

This application uses a "Logic Sandwich" architecture:
- **Rust Backend**: Core clipboard logic, SQLite storage, system hooks via UniFFI
- **C# Frontend**: WinUI 3 UI with desktop Acrylic backdrop, native Windows 11 look

## Prerequisites

### Development
- Windows 10/11
- .NET 10.0 SDK
- Visual Studio 2022 with WinUI 3 workload
- Rust toolchain (for building the backend)

### Runtime
- Windows 10 version 1809 (build 17763) or later
- Windows App SDK runtime

## Building

### Quick Build (PowerShell)
```powershell
# Debug build
.\scripts\build-windows-ui.ps1

# Release build
.\scripts\build-windows-ui.ps1 -Release

# Clean build with binding generation
.\scripts\build-windows-ui.ps1 -CleanBuild -GenerateBindings
```


### Manual Build

1. Build the Rust bridge library:
   ```bash
   cargo build --release -p clipcy-bridge
   ```

2. Generate C# bindings (requires uniffi-bindgen-cs):
   ```bash
   # Install uniffi-bindgen-cs first
   uniffi-bindgen-cs --library target/release/clipcy_core.dll --out-dir apps/clipcy-windows-ui/Bindings
   ```

3. Build the C# application:
   ```bash
   cd apps/clipcy-windows-ui
   dotnet build
   ```

## Project Structure

```
apps/clipcy-windows-ui/
├── Clipcy.csproj           # Project file
├── App.xaml(.cs)           # Application entry point
├── MainWindow.xaml(.cs)    # Main popup window
├── SettingsWindow.xaml(.cs)# Settings UI
├── ViewModels/
│   ├── MainViewModel.cs
│   └── ClipboardItemViewModel.cs
├── Services/
│   ├── RustBridgeService.cs  # Wrapper for Rust bindings
│   └── TrayService.cs        # System tray management
├── Converters/
│   └── BoolToVisibilityConverter.cs
├── Bindings/               # Generated UniFFI bindings
│   └── clipcy_core.cs      # (generated)
└── Assets/
    └── icon.ico
```

## Features

- **Desktop Acrylic**: Main clipboard window and item preview use `DesktopAcrylicBackdrop` (OS solid fallback when transparency/high contrast suppress the material)
- **System Tray**: Always-accessible tray icon with context menu
- **Global Hotkey**: Alt+V (configurable)
- **Search**: Fuzzy search through clipboard history
- **Preview Panel**: Desktop Acrylic popup beside the list for clipboard Text/Html/RTF/Image and **file** items. File previews read the **current** local path (not a history snapshot): images (PNG/JPEG/BMP/GIF/TIFF/ICO/JXR) as static aspect-fit PNG ≤1024px long edge; text (UTF-8 or BOM UTF-16, ≤64 KiB prefix with truncation notice); video first decoded presentation frame via Media Foundation (H.264 MP4 baseline; OS codecs for others); audio as a music-note icon only. Mixed multi-file selections show an original-order selector (default first). UNC/remote/cloud/reparse paths keep name/status without hydration. Paste still uses original CF_HDROP paths.
- **Theming**: Follows system light/dark theme

## Configuration

Settings are stored in `%LOCALAPPDATA%\clipcy\settings.json` and shared with the Rust backend.

## Known Issues

- UniFFI C# bindings require manual generation (automated tooling in progress)
- Hotkey capture in settings requires a restart to take effect

## License

MIT License - see project root LICENSE file.
