# Repository Guidelines

## Project Structure & Module Organization

Clipcy is a Rust workspace with native UI apps. Key paths:
- `crates/` Rust libraries (core domain, storage, platform adapters, bridge).
- `apps/` native UI apps (Windows WinUI app in `apps/clipcy-windows-ui/`; macOS/Linux placeholders).
- `scripts/` build and installer automation (PowerShell + Inno Setup).
- `public/`, `dist/`, `target/` generated assets/build outputs.

Start with `crates/core` for domain logic and ports. Platform-specific code lives in `crates/platform-windows`. The UniFFI bridge is in `crates/bridge`.

## Build, Test, and Development Commands

Core Rust:
- `cargo build --workspace` build all crates.
- `cargo build --workspace --release` release build.
- `cargo run -p clipcy-windows` run the Windows app (Rust entry).
- `cargo test --workspace` run all tests.

Windows UI:
- `.\scripts\build-windows-ui.ps1` build WinUI app (Debug).
- `.\scripts\build-windows-ui.ps1 -Release` release build.
- `.\scripts\build-windows-installer.ps1` build installer (Inno Setup required).
- `dotnet run` from `apps/clipcy-windows-ui/` for local UI runs.

Bridge/bindings:
- `cargo build --release -p clipcy-bridge`
- `uniffi-bindgen-cs --library target/release/clipcy_core.dll --out-dir apps/clipcy-windows-ui/Bindings`

## Coding Style & Naming Conventions

C# style is defined in `.editorconfig`: 4-space indentation, CRLF, max line length 120, and PascalCase for classes. Rust follows idiomatic formatting; keep platform code out of `crates/core`. Use `tracing` for logging, not `println!`.

## Testing Guidelines

Tests are Rust-based:
- Unit tests: `crates/core/src/domain/clipboard_item.rs`.
- Integration tests: `crates/storage/tests/repository_test.rs`.
Run with `cargo test --workspace` or a specific test name (example: `cargo test test_save_and_retrieve`).

## Commit & Pull Request Guidelines

No strict commit convention is enforced; recent history uses short, direct subjects (e.g., `fix: ...`, `update ...`, `Phase 1: ...`). Keep messages concise and descriptive.

PRs should include a brief summary and testing notes. For UI changes, add screenshots or a short clip and mention any platform-specific setup.

## Configuration & Local Data

Settings live at `%LOCALAPPDATA%\clipcy\settings.json` and the SQLite database at `%LOCALAPPDATA%\clipcy\clipcy.db`. Avoid committing local settings or generated outputs from `dist/` and `target/`.
