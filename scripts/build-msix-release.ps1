param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Version,
    [string]$Repo,
    [string]$CertificatePath,
    [string]$CertificatePassword
)

$ErrorActionPreference = "Stop"

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

if (-not $Repo) {
    throw "Repo is required. Example: ORG/REPO"
}

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$CargoToml = Join-Path $ProjectRoot "Cargo.toml"

if (-not $Version) {
    if (-not (Test-Path $CargoToml)) {
        throw "Version not provided and Cargo.toml not found at $CargoToml"
    }

    $Version = Get-WorkspaceVersion -Path $CargoToml
    if (-not $Version) {
        throw "Failed to read version from Cargo.toml"
    }
}

$versionParts = $Version.Split(".")
if ($versionParts.Length -eq 3) {
    $Version = "$Version.0"
} elseif ($versionParts.Length -ne 4) {
    throw "Version must be in 3 or 4-part format (e.g. 1.2.3 or 1.2.3.0)"
}

$tagVersion = ($Version.Split(".")[0..2] -join ".")

$CSharpProject = Join-Path $ProjectRoot "apps" "clipcy-windows-ui"
$PublishRoot = Join-Path $ProjectRoot "dist" "msix" $Runtime
$AssetsScript = Join-Path $ProjectRoot "scripts" "generate-msix-assets.ps1"

if (-not (Test-Path $AssetsScript)) {
    throw "Missing asset script at $AssetsScript"
}

if (-not (Test-Path $CertificatePath)) {
    throw "Certificate not found at $CertificatePath"
}

switch ($Runtime) {
    "win-x64" { $RustTarget = "x86_64-pc-windows-msvc" }
    "win-arm64" { $RustTarget = "aarch64-pc-windows-msvc" }
    default { throw "Unsupported runtime '$Runtime'. Use win-x64 or win-arm64." }
}

Write-Host "[1/4] Generating MSIX assets..." -ForegroundColor Yellow
& $AssetsScript

Write-Host "[2/4] Building Rust bridge library..." -ForegroundColor Yellow
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

Write-Host "[3/4] Publishing MSIX..." -ForegroundColor Yellow
Push-Location $CSharpProject
try {
    dotnet publish -c $Configuration -r $Runtime -p:PackageVersion=$Version -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=true -p:PackageCertificateKeyFile=$CertificatePath -p:PackageCertificatePassword=$CertificatePassword -p:AppInstallerUri=https://github.com/$Repo/releases/latest/download/Clipcy.appinstaller
}
finally {
    Pop-Location
}

$msix = Get-ChildItem -Path $CSharpProject -Filter *.msix -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) {
    throw "MSIX not found under $CSharpProject"
}

if (-not (Test-Path $PublishRoot)) {
    New-Item -ItemType Directory -Path $PublishRoot | Out-Null
}

$msixOut = Join-Path $PublishRoot "Clipcy.msix"
Copy-Item -Force $msix.FullName $msixOut

Write-Host "[4/4] Generating appinstaller..." -ForegroundColor Yellow
$templatePath = Join-Path $CSharpProject "Packaging" "Clipcy.appinstaller"
if (-not (Test-Path $templatePath)) {
    throw "Appinstaller template not found at $templatePath"
}

$appinstaller = Get-Content $templatePath -Raw
$appinstaller = $appinstaller -replace "ORG/REPO", $Repo
$appinstaller = $appinstaller -replace "v1.0.0", "v$tagVersion"
$appinstaller = $appinstaller -replace "1.0.0.0", $Version

$appinstallerOut = Join-Path $PublishRoot "Clipcy.appinstaller"
Set-Content -Path $appinstallerOut -Value $appinstaller -Encoding UTF8

Write-Host "MSIX output: $msixOut" -ForegroundColor Green
Write-Host "Appinstaller: $appinstallerOut" -ForegroundColor Green
