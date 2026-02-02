param(
    [string]$IconPath,
    [string]$AssetsDir
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$DefaultAssetsDir = Join-Path (Join-Path (Join-Path $ProjectRoot "apps") "clipcy-windows-ui") "Assets"
$ResolvedIconPath = if ($IconPath) { $IconPath } else { Join-Path $DefaultAssetsDir "icon.ico" }
$ResolvedAssetsDir = if ($AssetsDir) { $AssetsDir } else { $DefaultAssetsDir }

if (-not (Test-Path $ResolvedIconPath)) {
    throw "Icon not found at $ResolvedIconPath"
}

if (-not (Test-Path $ResolvedAssetsDir)) {
    New-Item -ItemType Directory -Path $ResolvedAssetsDir | Out-Null
}

Add-Type -AssemblyName System.Drawing

function Save-SquareIconPng {
    param(
        [string]$Path,
        [int]$Size
    )

    $icon = New-Object System.Drawing.Icon($ResolvedIconPath, $Size, $Size)
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $iconBmp = $icon.ToBitmap()
    $g.DrawImage($iconBmp, 0, 0, $Size, $Size)
    $g.Dispose()
    $iconBmp.Dispose()
    $icon.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Save-SplashPng {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height
    )

    $iconSize = [int]([Math]::Min($Width, $Height) * 0.6)
    $icon = New-Object System.Drawing.Icon($ResolvedIconPath, $iconSize, $iconSize)
    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $iconBmp = $icon.ToBitmap()
    $x = [int](($Width - $iconSize) / 2)
    $y = [int](($Height - $iconSize) / 2)
    $g.DrawImage($iconBmp, $x, $y, $iconSize, $iconSize)
    $g.Dispose()
    $iconBmp.Dispose()
    $icon.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Save-SquareIconPng -Path (Join-Path $ResolvedAssetsDir "Square44x44Logo.png") -Size 44
Save-SquareIconPng -Path (Join-Path $ResolvedAssetsDir "Square150x150Logo.png") -Size 150
Save-SquareIconPng -Path (Join-Path $ResolvedAssetsDir "StoreLogo.png") -Size 50
Save-SplashPng -Path (Join-Path $ResolvedAssetsDir "SplashScreen.png") -Width 620 -Height 300

Write-Host "MSIX assets generated in $ResolvedAssetsDir" -ForegroundColor Green
