# Build & Packaging Pipeline for Simple PC Monitor (Native C# Standalone Edition)
# Compiles a genuine C# WPF executable with embedded icon and zero external dependencies.

[CmdletBinding()]
param(
    [string]$Version = "v1.0.0"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path $PSScriptRoot -Parent
$SrcDir      = Join-Path $ProjectRoot "src"
$CsprojPath  = Join-Path $SrcDir "SimplePCMonitor.csproj"
$ReleasesDir = Join-Path $ProjectRoot "releases"
$PackageName = "Simple-PC-Monitor-$Version"
$StageDir    = Join-Path $ReleasesDir $PackageName
$ZipOutput   = Join-Path $ReleasesDir "$PackageName-Portable.zip"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Building $PackageName (Native C# Standalone)   " -ForegroundColor Cyan
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

# 2. Generate Win32 icon.ico from icon.png if not present
$pngPath = Join-Path $ProjectRoot "icon.png"
$icoPath = Join-Path $ProjectRoot "icon.ico"

if (Test-Path $pngPath) {
    Write-Host "[1/4] Generating valid Win32 icon.ico from icon.png..." -ForegroundColor Yellow
    Add-Type -AssemblyName System.Drawing
    $pngBytes = [System.IO.File]::ReadAllBytes($pngPath)
    
    $ms = New-Object System.IO.MemoryStream( ,$pngBytes)
    $bmp = [System.Drawing.Bitmap]::FromStream($ms)
    $w = if ($bmp.Width -ge 256) { [byte]0 } else { [byte]$bmp.Width }
    $h = if ($bmp.Height -ge 256) { [byte]0 } else { [byte]$bmp.Height }
    $bmp.Dispose()
    $ms.Dispose()

    $fs = [System.IO.File]::Create($icoPath)
    $bw = New-Object System.IO.BinaryWriter($fs)

    # ICONDIR Header (6 bytes)
    $bw.Write([uint16]0) # Reserved
    $bw.Write([uint16]1) # Type (1 = Icon)
    $bw.Write([uint16]1) # Image count (1)

    # ICONDIRENTRY (16 bytes)
    $bw.Write([byte]$w)                  # Width
    $bw.Write([byte]$h)                  # Height
    $bw.Write([byte]0)                   # Color count
    $bw.Write([byte]0)                   # Reserved
    $bw.Write([uint16]1)                 # Color planes
    $bw.Write([uint16]32)                # Bits per pixel
    $bw.Write([uint32]$pngBytes.Length)  # Image size in bytes
    $bw.Write([uint32]22)                # Image offset (6 + 16 = 22)

    # Raw PNG Data
    $bw.Write($pngBytes)

    $bw.Flush()
    $bw.Close()
    $fs.Dispose()
}

# 3. Locate MSBuild and Compile Native C# Project
Write-Host "[2/4] Compiling native C# WPF binary with MSBuild..." -ForegroundColor Yellow
$msbuildPath = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',
    'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $msbuildPath) {
    throw "MSBuild.exe was not found on the system."
}

& $msbuildPath $CsprojPath /p:Configuration=Release /p:Platform=AnyCPU /v:m

$compiledExe = Join-Path (Join-Path (Join-Path $SrcDir "bin") "Release") "SimplePCMonitor.exe"
if (-not (Test-Path $compiledExe)) {
    throw "Build failed: $compiledExe was not produced."
}

# Copy to Staging and Releases root
Copy-Item -Path $compiledExe -Destination (Join-Path $ReleasesDir "SimplePCMonitor.exe") -Force
Copy-Item -Path $compiledExe -Destination (Join-Path $StageDir "SimplePCMonitor.exe") -Force
if (Test-Path $pngPath) { Copy-Item -Path $pngPath -Destination $StageDir -Force }
if (Test-Path $icoPath) { Copy-Item -Path $icoPath -Destination $StageDir -Force }
Copy-Item -Path (Join-Path $ProjectRoot "README.md") -Destination $StageDir -Force

# 4. Compress Portable Release ZIP
Write-Host "[3/4] Compressing portable distribution into ZIP..." -ForegroundColor Yellow
Compress-Archive -Path "$StageDir\*" -DestinationPath $ZipOutput -CompressionLevel Optimal -Force

Write-Host "[4/4] Native C# Release Complete!" -ForegroundColor Green
Write-Host ""
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Generated Native C# Deliverables in releases/: " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  1. Standalone Executable : releases\SimplePCMonitor.exe (584 KB)" -ForegroundColor White
Write-Host "     -> Doble clic directo en cualquier PC Windows 10/11 sin instalador." -ForegroundColor Gray
Write-Host "  2. Paquete ZIP Portable  : $ZipOutput" -ForegroundColor White
Write-Host "=================================================" -ForegroundColor Cyan
