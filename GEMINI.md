# Clipcy - Context for Gemini

## Project Overview

**Clipcy** is a modern, cross-platform clipboard manager with a hybrid architecture. It combines a high-performance **Rust** backend with native user interfaces for each platform (currently focusing on **Windows via WinUI 3**).

### Architecture

The project follows a "Logic Sandwich" or Hexagonal architecture:

1.  **Rust Core (`crates/core`)**: Contains the business logic, domain models, and ports (interfaces). It is completely UI-agnostic.
2.  **Storage (`crates/storage`)**: SQLite implementation for persisting clipboard history.
3.  **Bridge (`crates/bridge`)**: Defines the interface between Rust and the native UIs using **UniFFI**. It exposes the core logic as a C-compatible library (`clipcy_core.dll`).
4.  **Native UI (`apps/clipcy-windows-ui`)**: A **WinUI 3** application written in **C# (.NET 8)**. It consumes the Rust logic via generated bindings.

### Key Directories

*   **`apps/clipcy-windows-ui/`**: The Windows client application (C#, XAML).
*   **`crates/`**: The Rust workspace containing all backend modules.
    *   `bridge/`: UniFFI definitions (`clipcy_core.udl`) and library entry point.
    *   `core/`: Core domain logic (history management, search, etc.).
    *   `platform-windows/`: Windows-specific Rust implementations (hooks, tray, theme detection).
    *   `storage/`: Database access layer (SQLx/SQLite).
*   **`scripts/`**: PowerShell and Batch scripts for building and deployment.

## Development Setup & Workflow

### Prerequisites

*   **Rust**: Stable toolchain (1.75+).
*   **C#/.NET**: .NET 8 SDK.
*   **Visual Studio 2022**: With "Windows App SDK" workload.
*   **UniFFI Bindings Tool**: `uniffi-bindgen-cs` (needed if modifying the bridge).

### Building the Project (Windows)

The primary build script is `scripts/build-windows-ui.ps1`.

**Standard Debug Build:**
```powershell
.\scripts\build-windows-ui.ps1
```
This builds the Rust bridge (`debug`), restores NuGet packages, and builds the WinUI app (`debug`).

**Release Build:**
```powershell
.\scripts\build-windows-ui.ps1 -Release
```
This performs a release build and creates a portable ZIP archive.

**Clean Build:**
```powershell
.\scripts\build-windows-ui.ps1 -CleanBuild
```

**Generating Bindings:**
If you modify the UniFFI interface (`crates/bridge/src/clipcy_core.udl`):
```powershell
.\scripts\build-windows-ui.ps1 -GenerateBindings
```
*Note: This requires `uniffi-bindgen-cs` to be installed.*

### Running the App

After building, the executable is located at:
*   **Debug**: `apps/clipcy-windows-ui/bin/Debug/net8.0-windows10.0.17763.0/Clipcy.exe`
*   **Release**: `apps/clipcy-windows-ui/bin/Release/net8.0-windows10.0.17763.0/win-x64/publish/Clipcy.exe`

### Common Tasks

*   **Modifying Core Logic**: Edit files in `crates/core`. Run `cargo check` in `crates/` to verify.
*   **Modifying UI**: Edit `.xaml` and `.cs` files in `apps/clipcy-windows-ui`.
*   **Adding a New Feature**:
    1.  Implement logic in `crates/core`.
    2.  Expose it in `crates/bridge/src/clipcy_core.udl` and `lib.rs`.
    3.  Regenerate bindings.
    4.  Update UI in `apps/clipcy-windows-ui` to use the new API.

## Conventions

*   **Rust**: Follows standard `clippy` and `rustfmt` guidelines.
*   **C#**: Follows standard .NET coding conventions.
*   **Architecture**: Strict separation of concerns. UI should not contain business logic; it should only display data provided by the Rust backend and delegate user actions to it.
