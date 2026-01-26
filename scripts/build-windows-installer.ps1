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
$OutputDir = Join-Path $ProjectRoot "dist" "installer"
$IssPath = Join-Path $ProjectRoot "scripts" "clipcy-installer.iss"
$IconPath = Join-Path $CSharpProject "Assets" "icon.ico"
$AppPublisher = "ThanhDA"

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
    Write-Host "`n[1/4] Cleaning output directories..." -ForegroundColor Yellow

    if (Test-Path $PublishDir) {
        Remove-Item -Recurse -Force $PublishDir
    }

    if (Test-Path $OutputDir) {
        Remove-Item -Recurse -Force $OutputDir
    }
}

Write-Host "`n[2/4] Building Rust bridge library..." -ForegroundColor Yellow
Push-Location $ProjectRoot
try {
    cargo build --release -p clipcy-bridge
}
finally {
    Pop-Location
}

Write-Host "`n[3/4] Publishing WinUI application..." -ForegroundColor Yellow
if (-not (Test-Path $PublishDir)) {
    New-Item -ItemType Directory -Path $PublishDir | Out-Null
}

Push-Location $CSharpProject
try {
    dotnet publish -c $Configuration -r $Runtime -o $PublishDir
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

Write-Host "`n[4/4] Building installer with Inno Setup..." -ForegroundColor Yellow
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

Write-Host "Debug - PublishDir: $PublishDir" -ForegroundColor Gray
Write-Host "Debug - OutputDir: $OutputDir" -ForegroundColor Gray
Write-Host "Debug - AppVersion: $AppVersion" -ForegroundColor Gray

$isccArgs = @(
    "/DPublishDir=`"$PublishDir`"",
    "/DOutputDir=`"$OutputDir`"",
    "/DAppVersion=`"$AppVersion`"",
    "/DIconPath=`"$IconPath`"",
    "/DAppPublisher=`"$AppPublisher`"",
    $IssPath
)

& $IsccPath $isccArgs

Write-Host "`n=== Installer Build Complete ===" -ForegroundColor Green
Write-Host "Publish Dir: $PublishDir" -ForegroundColor Cyan
Write-Host "Installer Dir: $OutputDir" -ForegroundColor Cyan
