# Clipcy - Project Context

## Project Overview
Clipcy is a modern, cross-platform clipboard manager utilizing a hybrid architecture. It combines a high-performance **Rust backend** for core logic, storage, and system hooks with **native UIs** for each platform to ensure a seamless user experience.

The architecture is described as a "Logic Sandwich":
1.  **Rust Backend:** Handles clipboard monitoring, data storage (SQLite), hotkeys, and business logic.
2.  **UniFFI Bridge:** Exposes the Rust core to native languages (C#, Swift, etc.).
3.  **Native UI:** Platform-specific implementations (WinUI 3 for Windows, SwiftUI for macOS).

## Tech Stack
*   **Core:** Rust (Edition 2024)
*   **Windows UI:** C#, .NET 8, WinUI 3 (Windows App SDK)
*   **Bridge:** UniFFI (Rust -> C# Bindings)
*   **Storage:** SQLite (via `sqlx`)
*   **Asynchronous Runtime:** Tokio

## Directory Structure
The project follows a monorepo-like structure:

*   **`crates/`** (Rust Workspace):
    *   `core`: Domain models, ports, and application services.
    *   `storage`: SQLite repository implementation.
    *   `bridge`: The UniFFI interface definition (`.udl`) and library entry point.
    *   `platform-*`: Platform-specific implementations (e.g., Windows hooks).
    *   `settings-common`: Shared settings logic.
*   **`apps/`** (Frontend Applications):
    *   `clipcy-windows-ui`: The Windows client (C#/.NET 8/WinUI 3).
    *   `clipcy-macos`: macOS client (Planned/Placeholder).
    *   `clipcy-linux`: Linux client (Planned/Placeholder).
*   **`scripts/`**: Helper scripts for building and maintenance (e.g., `build-windows-ui.ps1`).

## Building and Running

### Prerequisites
*   **Rust:** 1.75+
*   **Windows:** .NET 8 SDK, Visual Studio 2022 with "Windows App SDK" workload.

### Windows Workflow
The primary build script handles both the Rust core compilation and the .NET application build.

**Using PowerShell Script:**
```powershell
# Debug Build
.\scripts\build-windows-ui.ps1

# Release Build
.\scripts\build-windows-ui.ps1 -Release

# Clean Build
.\scripts\build-windows-ui.ps1 -CleanBuild
```

**Manual Steps:**
1.  **Build Rust Core:**
    ```bash
    cargo build --release -p clipcy-bridge
    ```
    *Output:* `target/release/clipcy_core.dll`

2.  **Generate Bindings (if .udl changed):**
    *   Requires `uniffi-bindgen-cs`.
    *   Bindings are placed in `apps/clipcy-windows-ui/Bindings`.

3.  **Run Windows App:**
    ```bash
    cd apps/clipcy-windows-ui
    dotnet run
    ```

## Development Conventions

*   **Architecture:** Hexagonal (Ports & Adapters).
    *   **Core:** Pure Rust, no UI dependencies. Defines traits (ports) for platform interaction.
    *   **Adapters:** Implementations in `platform-*` and `storage` crates.
*   **Logging:** Tracing is used in Rust. Logs are stored in `%LOCALAPPDATA%\clipcy\logs\`.
*   **Database:** `sqlx` with SQLite. Database file at `%LOCALAPPDATA%\clipcy\clipcy.db`.
*   **UI Pattern:** MVVM (Model-View-ViewModel) is used in the C# WinUI project (`CommunityToolkit.Mvvm`).

## Key Configuration Files
*   `Cargo.toml`: Rust workspace definition.
*   `apps/clipcy-windows-ui/Clipcy.csproj`: Windows app project file.
*   `crates/bridge/src/clipcy_core.udl`: UniFFI definition language file (API contract between Rust and C#).
