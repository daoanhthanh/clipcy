# Build script for Clipcy Windows UI
# This script builds both the Rust backend library and the C# WinUI 3 application

param(
    [switch]$Release,
    [switch]$GenerateBindings,
    [switch]$CleanBuild
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$RustProject = $ProjectRoot
$CSharpProject = Join-Path $ProjectRoot "apps" "clipcy-windows-ui"
$BindingsDir = Join-Path $CSharpProject "Bindings"

Write-Host "=== Clipcy Windows UI Build ===" -ForegroundColor Cyan
Write-Host "Project Root: $ProjectRoot"

# Clean if requested
if ($CleanBuild) {
    Write-Host "`n[1/5] Cleaning previous builds..." -ForegroundColor Yellow

    if (Test-Path (Join-Path $RustProject "target")) {
        Write-Host "  Cleaning Rust target directory..."
        cargo clean --manifest-path (Join-Path $RustProject "Cargo.toml")
    }

    if (Test-Path (Join-Path $CSharpProject "bin")) {
        Write-Host "  Cleaning C# bin directory..."
        Remove-Item -Recurse -Force (Join-Path $CSharpProject "bin")
    }

    if (Test-Path (Join-Path $CSharpProject "obj")) {
        Write-Host "  Cleaning C# obj directory..."
        Remove-Item -Recurse -Force (Join-Path $CSharpProject "obj")
    }
}

# Build Rust bridge library
Write-Host "`n[2/5] Building Rust bridge library..." -ForegroundColor Yellow
Push-Location $RustProject
try {
    if ($Release) {
        cargo build --release -p clipcy-bridge
        $RustOutputDir = Join-Path $RustProject "target" "release"
    } else {
        cargo build -p clipcy-bridge
        $RustOutputDir = Join-Path $RustProject "target" "debug"
    }

    $DllPath = Join-Path $RustOutputDir "clipcy_core.dll"
    if (-not (Test-Path $DllPath)) {
        throw "Failed to build Rust library - clipcy_core.dll not found"
    }
    Write-Host "  Built: $DllPath" -ForegroundColor Green
}
finally {
    Pop-Location
}

# Generate UniFFI bindings if requested
if ($GenerateBindings) {
    Write-Host "`n[3/5] Generating UniFFI C# bindings..." -ForegroundColor Yellow

    # Ensure bindings directory exists
    if (-not (Test-Path $BindingsDir)) {
        New-Item -ItemType Directory -Path $BindingsDir | Out-Null
    }

    # Install uniffi-bindgen-cs if not present
    # Note: This requires the uniffi-bindgen-cs tool to be installed
    # cargo install uniffi-bindgen-cs

    Push-Location $RustProject
    try {
        # Generate C# bindings using uniffi-bindgen
        # The actual command depends on how uniffi-bindgen-cs is set up
        # This is a placeholder - adjust based on actual setup

        Write-Host "  Note: UniFFI C# binding generation requires manual setup"
        Write-Host "  See: https://github.com/aspect-build/aspect-workflows/tree/main/packages/aspect/tools/uniffi-bindgen-cs"

        # Example command (requires uniffi-bindgen-cs):
        # uniffi-bindgen-cs --library $DllPath --out-dir $BindingsDir
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "`n[3/5] Skipping binding generation (use -GenerateBindings to generate)" -ForegroundColor Gray
}

# Restore NuGet packages
Write-Host "`n[4/5] Restoring NuGet packages..." -ForegroundColor Yellow
Push-Location $CSharpProject
try {
    dotnet restore
}
finally {
    Pop-Location
}

# Build C# application
Write-Host "`n[5/5] Building C# WinUI 3 application..." -ForegroundColor Yellow
Push-Location $CSharpProject
try {
    if ($Release) {
        dotnet publish -c Release
    } else {
        dotnet build -c Debug
    }
}
finally {
    Pop-Location
}

Write-Host "`n=== Build Complete ===" -ForegroundColor Green

$OutputDir = if ($Release) { "Release" } else { "Debug" }
$ExePath = if ($Release) {
    # Check standard publish output locations
    $Win10Path = Join-Path $CSharpProject "bin" "Release" "net8.0-windows10.0.17763.0" "win-x64" "publish" "Clipcy.exe"
    if (Test-Path $Win10Path) { $Win10Path } else {
        # Fallback to standard build output if publish path structure differs
        Join-Path $CSharpProject "bin" "Release" "net8.0-windows10.0.17763.0" "Clipcy.exe"
    }
} else {
    Join-Path $CSharpProject "bin" "Debug" "net8.0-windows10.0.17763.0" "Clipcy.exe"
}

if (Test-Path $ExePath) {
    Write-Host "Output: $ExePath" -ForegroundColor Cyan
} else {
    Write-Host "Warning: Expected output not found at typical location. Check the build logs above for the specific output path." -ForegroundColor Yellow
}
