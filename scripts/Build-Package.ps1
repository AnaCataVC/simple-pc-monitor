# Build & Packaging Pipeline for System Core Monitor (Native C# Standalone & Setup Wizard Edition)
# Compiles a genuine C# WPF standalone executable for .NET 9 with zero third-party dependencies.

[CmdletBinding()]
param(
    [string]$Version = "v3.0.0"
)

$ErrorActionPreference = "Stop"

$ProjectRoot   = Split-Path $PSScriptRoot -Parent
$SrcDir        = Join-Path $ProjectRoot "src"
$AppCsprojPath = Join-Path $SrcDir "SystemCoreMonitor.csproj"
$InstCsproj    = Join-Path (Join-Path $SrcDir "Installer") "Installer.csproj"
$ReleasesDir   = Join-Path $ProjectRoot "releases"
$PackageName   = "System-Core-Monitor-$Version"
$StageDir      = Join-Path $ReleasesDir $PackageName
$ZipOutput     = Join-Path $ReleasesDir "$PackageName-Portable.zip"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Building $PackageName (.NET 9 Standalone)      " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

# 1. Ensure releases directory exists
if (-not (Test-Path $ReleasesDir)) {
    New-Item -Path $ReleasesDir -ItemType Directory | Out-Null
}

if (-not (Test-Path $StageDir)) {
    New-Item -Path $StageDir -ItemType Directory | Out-Null
}

if (Test-Path $ZipOutput) {
    Remove-Item -Path $ZipOutput -Force -ErrorAction SilentlyContinue
}

# 2. Generate Win32 icon.ico from icon.png
$pngPath = Join-Path $ProjectRoot "icon.png"
$icoPath = Join-Path $ProjectRoot "icon.ico"

if (Test-Path $pngPath) {
    Write-Host "[1/4] Generating multi-resolution Win32 icon.ico from icon.png..." -ForegroundColor Yellow
    Add-Type -AssemblyName System.Drawing
    $srcBmp = [System.Drawing.Bitmap]::FromFile($pngPath)

    $sizes = @(256, 128, 64, 48, 32, 16)
    $pngFrames = @()

    foreach ($sz in $sizes) {
        $resized = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($resized)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.DrawImage($srcBmp, 0, 0, $sz, $sz)
        $g.Dispose()

        $ms = New-Object System.IO.MemoryStream
        $resized.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $resized.Dispose()
        $pngFrames += @{ Size = $sz; Bytes = $ms.ToArray() }
        $ms.Dispose()
    }
    $srcBmp.Dispose()

    $fs = [System.IO.File]::Create($icoPath)
    $bw = New-Object System.IO.BinaryWriter($fs)

    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$pngFrames.Count)

    $offset = 6 + (16 * $pngFrames.Count)
    foreach ($img in $pngFrames) {
        $w = if ($img.Size -ge 256) { [byte]0 } else { [byte]$img.Size }
        $h = if ($img.Size -ge 256) { [byte]0 } else { [byte]$img.Size }
        $bw.Write([byte]$w)
        $bw.Write([byte]$h)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$img.Bytes.Length)
        $bw.Write([uint32]$offset)
        $offset += $img.Bytes.Length
    }

    foreach ($img in $pngFrames) {
        $bw.Write($img.Bytes)
    }

    $bw.Flush()
    $bw.Close()
    $fs.Dispose()
}

# 3. Compile Standalone Main Executable (.NET 9 Publish)
Write-Host "[2/4] Publishing standalone C# WPF binary with dotnet..." -ForegroundColor Yellow
$publishDir = Join-Path (Join-Path $SrcDir "bin") "Publish"
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
}

& dotnet publish $AppCsprojPath -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "Build failed: dotnet publish exited with code $LASTEXITCODE"
}

$compiledExe = Join-Path $publishDir "SystemCoreMonitor.exe"
if (-not (Test-Path $compiledExe)) {
    throw "Build failed: $compiledExe was not produced."
}

# Copy to Releases and Staging
$targetReleaseExe = Join-Path $ReleasesDir "SystemCoreMonitor.exe"
Copy-Item -Path $compiledExe -Destination $targetReleaseExe -Force
Copy-Item -Path $compiledExe -Destination (Join-Path $StageDir "SystemCoreMonitor.exe") -Force

# For backward compatibility with legacy tests or tools
Copy-Item -Path $compiledExe -Destination (Join-Path $ReleasesDir "SimplePCMonitor.exe") -Force

if (Test-Path $pngPath) { Copy-Item -Path $pngPath -Destination $StageDir -Force }
if (Test-Path $icoPath) { Copy-Item -Path $icoPath -Destination $StageDir -Force }
Copy-Item -Path (Join-Path $ProjectRoot "README.md") -Destination $StageDir -Force

# 4. Compress Portable Release ZIP
Write-Host "[3/4] Compressing portable distribution into ZIP..." -ForegroundColor Yellow
Compress-Archive -Path "$StageDir\*" -DestinationPath $ZipOutput -CompressionLevel Optimal -Force
$unversionedZip = Join-Path $ReleasesDir "System-Core-Monitor-Portable.zip"
Copy-Item -Path $ZipOutput -Destination $unversionedZip -Force

Write-Host "[4/4] Native C# Deliverables Ready!" -ForegroundColor Green
Write-Host ""
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Generated Native C# Deliverables in releases/: " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  1. Standalone Executable : $targetReleaseExe" -ForegroundColor White
Write-Host "     -> Doble clic directo en cualquier PC Windows 10/11 con .NET 9 Desktop Runtime." -ForegroundColor Gray

$sizeKB = [math]::Round((Get-Item $targetReleaseExe).Length / 1KB)
Write-Host "     -> Tamano medido      : $sizeKB KB" -ForegroundColor Gray

Write-Host "  2. Paquete ZIP Portable  : $ZipOutput" -ForegroundColor White
Write-Host "     -> Copia generica     : $unversionedZip" -ForegroundColor Gray
Write-Host "=================================================" -ForegroundColor Cyan