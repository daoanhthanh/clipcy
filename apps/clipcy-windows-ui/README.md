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
   cargo build -p clipcy-bridge
   ```

2. Generate C# bindings (requires uniffi-bindgen-cs):
   ```bash
   # Install uniffi-bindgen-cs first
   uniffi-bindgen-cs --library target/debug/clipcy_core.dll --out-dir apps/clipcy-windows-ui/Bindings
   ```

3. Build the C# application:
   ```bash
   cd apps/clipcy-windows-ui
   dotnet build
   ```

Debug UI builds load `target/debug/clipcy_core.dll`; Release UI builds load `target/release/clipcy_core.dll`.
For a release build, use `cargo build --release -p clipcy-bridge` followed by `dotnet build -c Release`.
Restart Clipcy after rebuilding so it loads the updated backend.

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
- **Settings app branding**: The title bar and About tab display the Clipcy app logo from `Assets/Square44x44Logo.png`.
- **Global Hotkey**: Alt+V (configurable)
- **Search**: Fuzzy search through clipboard history
- **Quick paste**: Ctrl+1 through Ctrl+9, then Ctrl+0, paste the first ten displayed regular rows, including search results. Number-row and numpad digits work; shortcut hints appear on the right. Modifier keys must be released before Clipcy sends the paste.
- **Popup scrolling**: Opening the popup resets both the pinned and regular lists to the top.
- **Pinning**: Hover/focus a history row or select it with Up/Down, then press **Ctrl+P** to pin/unpin it (also available in the row's context menu). Pinned items sit in their own region above the regular list, separated by a horizontal line. Each region scrolls on its own. The pinned region shows at most 3 rows; more pins scroll inside it, and the popup grows by that region's height so the regular list keeps its space. Selection follows the toggled item when it moves between regions, so pressing **Ctrl+P** again unpins the same item; pinning does not clear the search filter.
- **Selection**: Only one row is active across both regions. Mouse movement/hover and Up/Down replace each other's selection; the latest input wins. Scrolling a row underneath a stationary cursor does not override arrow selection. Enter, Delete, and Ctrl+P target that one active row.
- **Preview Panel**: Desktop Acrylic popup beside the list for clipboard Text/Html/RTF/Image and **file** items. File previews read the **current** local path (not a history snapshot): images (PNG/JPEG/BMP/GIF/TIFF/ICO/JXR) as static aspect-fit PNG ≤1024px long edge; text (UTF-8 or BOM UTF-16, ≤64 KiB prefix with truncation notice); video first decoded presentation frame via Media Foundation (H.264 MP4 baseline; OS codecs for others); audio as a music-note icon only. Mixed multi-file selections show an original-order selector (default first). UNC/remote/cloud/reparse paths keep name/status without hydration. Paste still uses original CF_HDROP paths.
- **Preview setting**: Hover previews obey **Show preview panel**, including the saved value at startup. Turning it off closes an open preview and cancels pending hover previews; turning it back on enables previews on subsequent hovers without restarting.
- **Theming**: Follows system light/dark theme
- **Languages**: English, Simplified Chinese, Japanese, Russian, Korean, and Vietnamese. Change language in **Settings → General → Language**; open windows, dialogs, previews, and the tray menu update without restarting.

## Configuration

Settings are stored in `%LOCALAPPDATA%\clipcy\settings.json` and shared with the Rust backend.

The **Show notifications** control and its C# toggle handling are commented out; the saved notification preference is unchanged.

The frontend language preference is stored separately in `%LOCALAPPDATA%\clipcy\ui-settings.json`:

```json
{ "language": "en" }
```

Supported codes: `en`, `zh-CN`, `ja`, `ru`, `ko`, `vi`. Without a valid saved preference, Clipcy uses the
system UI language when supported, otherwise English. **Reset to Defaults** restores that system-language
default. If saving fails, the selector returns to the previous language and displays an error.

### Localization resources

`Localization/*.json` contains the six embedded catalogs. English source text is the lookup key; keep the
same keys and composite-format placeholders (including format specifiers) in every catalog.
Use `Services.Localization` attached properties for static XAML labels and
`LocalizationService.Get` / `Format` for dynamic text. Retain source keys and arguments for live refresh;
do not cache translated status strings. Clipboard contents, file names, hotkey tokens, and backend type
discriminators are not translated. Cached preview diagnostics retain their format keys and arguments so
switching language does not reload file contents.

## Known Issues

- UniFFI C# bindings require manual generation (automated tooling in progress)
- Hotkey capture in settings requires a restart to take effect

## License

MIT License - see project root LICENSE file.
