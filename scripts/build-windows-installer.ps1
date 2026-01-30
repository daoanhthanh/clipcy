param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$Clean,
    [string]$InnoSetupPath
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$CSharpProject = Join-Path $ProjectRoot "apps" "clipcy-windows-ui"
$CargoToml = Join-Path $ProjectRoot "Cargo.toml"
$PublishDir = Join-Path $ProjectRoot "dist" "windows" $Runtime
$OutputDir = Join-Path $ProjectRoot "dist" "installer" $Runtime
$IssPath = Join-Path $ProjectRoot "scripts" "clipcy-installer.iss"
$IconPath = Join-Path $CSharpProject "Assets" "icon.ico"
$AppPublisher = "Dao Anh Thanh (Đào Anh Thành)"

$Platform = $null
$AppArch = $null
$ArchAllowed = $null
$ArchInstallIn64BitMode = $null
$RustTarget = $null

switch ($Runtime) {
    "win-x64" {
        $Platform = "x64"
        $AppArch = "x64"
        $ArchAllowed = "x64"
        $ArchInstallIn64BitMode = "x64"
        $RustTarget = "x86_64-pc-windows-msvc"
    }
    "win-arm64" {
        $Platform = "ARM64"
        $AppArch = "arm64"
        $ArchAllowed = "arm64"
        $ArchInstallIn64BitMode = "arm64"
        $RustTarget = "aarch64-pc-windows-msvc"
    }
    default {
        throw "Unsupported runtime '$Runtime'. Use win-x64 or win-arm64."
    }
}

function Get-WorkspaceVersion {
    param([string]$Path)

    $inSection = $false
    foreach ($line in Get-Content $Path) {
        if ($line -match '^\[workspace\.package\]') {
            $inSection = $true
            continue
        }

        if ($inSection -and $line -match '^\[') {
            break
        }

        if ($inSection -and $line -match '^\s*version\s*=\s*"([^"]+)"') {
            return $Matches[1]
        }
    }

    return "0.0.0"
}

function Resolve-IsccPath {
    param([string]$Hint)

    if ($Hint) {
        if (Test-Path $Hint -PathType Container) {
            $candidate = Join-Path $Hint "ISCC.exe"
            if (Test-Path $candidate -PathType Leaf) {
                return $candidate
            }
        }

        if (Test-Path $Hint -PathType Leaf) {
            return $Hint
        }
    }

    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Path
    }

    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

Write-Host "=== Clipcy Windows Installer Build ===" -ForegroundColor Cyan
Write-Host "Project Root: $ProjectRoot"

if ($Clean) {
    Write-Host "`n[1/5] Cleaning output directories..." -ForegroundColor Yellow

    if (Test-Path $PublishDir) {
        Remove-Item -Recurse -Force $PublishDir
    }

    if (Test-Path $OutputDir) {
        Remove-Item -Recurse -Force $OutputDir
    }
}

Write-Host "`n[2/5] Building Backend (Rust) bridge library..." -ForegroundColor Yellow
Push-Location $ProjectRoot
try {
    cargo build --release -p clipcy-bridge --target $RustTarget
}
finally {
    Pop-Location
}

$RustOutputDir = Join-Path $ProjectRoot "target" $RustTarget "release"
$RustDll = Join-Path $RustOutputDir "clipcy_core.dll"
$RustDestDir = Join-Path $ProjectRoot "target" "release"
$RustDestDll = Join-Path $RustDestDir "clipcy_core.dll"

if (-not (Test-Path $RustDll)) {
    throw "Rust build failed - clipcy_core.dll not found at $RustDll"
}

if (-not (Test-Path $RustDestDir)) {
    New-Item -ItemType Directory -Path $RustDestDir | Out-Null
}

Copy-Item -Force $RustDll $RustDestDll

Write-Host "`n[3/5] Regenerating C# bindings..." -ForegroundColor Yellow
$BindingsDir = Join-Path $CSharpProject "Bindings"
if (-not (Test-Path $BindingsDir)) {
    New-Item -ItemType Directory -Path $BindingsDir | Out-Null
}

$uniffiBindgen = Get-Command "uniffi-bindgen-cs" -ErrorAction SilentlyContinue
if (-not $uniffiBindgen) {
    throw "uniffi-bindgen-cs not found. Install it with: cargo install uniffi-bindgen-cs"
}

try {
    & uniffi-bindgen-cs --library $RustDestDll --out-dir $BindingsDir
    Write-Host "C# bindings regenerated successfully" -ForegroundColor Green
}
catch {
    throw "Failed to generate C# bindings: $_"
}

Write-Host "`n[4/5] Publishing Clipcy application..." -ForegroundColor Yellow
if (-not (Test-Path $PublishDir)) {
    New-Item -ItemType Directory -Path $PublishDir | Out-Null
}

Push-Location $CSharpProject
try {
    dotnet publish -c $Configuration -r $Runtime -o $PublishDir -p:Platform=$Platform -p:PublishTrimmed=true -p:PublishSingleFile=false
}
finally {
    Pop-Location
}

$ExePath = Join-Path $PublishDir "Clipcy.exe"
if (-not (Test-Path $ExePath)) {
    throw "Publish failed - Clipcy.exe not found at $ExePath"
}

$AppVersion = Get-WorkspaceVersion -Path $CargoToml
$IsccPath = Resolve-IsccPath -Hint $InnoSetupPath
if (-not $IsccPath) {
    throw "Inno Setup compiler not found. Install Inno Setup 6 and ensure ISCC.exe is on PATH, or pass -InnoSetupPath."
}

Write-Host "`n[5/5] Building installer with Inno Setup..." -ForegroundColor Yellow
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

Write-Host "Debug - PublishDir: $PublishDir" -ForegroundColor Gray
Write-Host "Debug - OutputDir: $OutputDir" -ForegroundColor Gray
Write-Host "Debug - AppVersion: $AppVersion" -ForegroundColor Gray

$isccArgs = @(
    "/DPublishDir=$PublishDir",
    "/DOutputDir=$OutputDir",
    "/DAppVersion=$AppVersion",
    "/DIconPath=$IconPath",
    "/DAppPublisher=$AppPublisher",
    "/DAppArch=$AppArch",
    "/DArchAllowed=$ArchAllowed",
    "/DArchInstallIn64BitMode=$ArchInstallIn64BitMode",
    $IssPath
)

& $IsccPath $isccArgs

Write-Host "`n=== Installer Build Complete ===" -ForegroundColor Green
Write-Host "Publish Dir: $PublishDir" -ForegroundColor Cyan
Write-Host "Installer Dir: $OutputDir" -ForegroundColor Cyan
