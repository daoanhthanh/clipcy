# Build script for Windows
$ErrorActionPreference = "Stop"

Write-Host "Building Clipboard Manager for Windows..."

# Build release binary
cargo build --release --package clipcy-windows

# Create distribution folder
$dist = "dist/windows"
New-Item -ItemType Directory -Force -Path $dist

# Copy binary
Copy-Item "target/release/clipcy.exe" "$dist/"

# Create installer (optional - requires WiX or NSIS)
# & "C:\Program Files (x86)\WiX Toolset v3.11\bin\candle.exe" installer.wxs
# & "C:\Program Files (x86)\WiX Toolset v3.11\bin\light.exe" installer.wixobj

Write-Host "Build complete! Binary at $dist/clipcy.exe"
