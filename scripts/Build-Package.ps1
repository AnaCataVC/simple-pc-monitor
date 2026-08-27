# Build & Packaging Pipeline for Simple PC Monitor (Native C# Standalone & Setup Wizard Edition)
# Compiles a genuine C# WPF standalone executable and an installer setup wizard with zero external dependencies.

[CmdletBinding()]
param(
    [string]$Version = "v1.1.0"
)

$ErrorActionPreference = "Stop"

$ProjectRoot   = Split-Path $PSScriptRoot -Parent
$SrcDir        = Join-Path $ProjectRoot "src"
$AppCsprojPath = Join-Path $SrcDir "SimplePCMonitor.csproj"
$InstCsproj    = Join-Path (Join-Path $SrcDir "Installer") "Installer.csproj"
$ReleasesDir   = Join-Path $ProjectRoot "releases"
$PackageName   = "Simple-PC-Monitor-$Version"
$StageDir      = Join-Path $ReleasesDir $PackageName
$ZipOutput     = Join-Path $ReleasesDir "$PackageName-Portable.zip"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Building $PackageName (Standalone & Setup)     " -ForegroundColor Cyan
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
    Write-Host "[1/5] Generating multi-resolution Win32 icon.ico from icon.png..." -ForegroundColor Yellow
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

    # ICONDIR Header (6 bytes)
    $bw.Write([uint16]0) # Reserved
    $bw.Write([uint16]1) # Type (1 = Icon)
    $bw.Write([uint16]$pngFrames.Count) # Image count

    # ICONDIRENTRY (16 bytes each)
    $offset = 6 + (16 * $pngFrames.Count)
    foreach ($img in $pngFrames) {
        $w = if ($img.Size -ge 256) { [byte]0 } else { [byte]$img.Size }
        $h = if ($img.Size -ge 256) { [byte]0 } else { [byte]$img.Size }
        $bw.Write([byte]$w)                  # Width
        $bw.Write([byte]$h)                  # Height
        $bw.Write([byte]0)                   # Color count
        $bw.Write([byte]0)                   # Reserved
        $bw.Write([uint16]1)                 # Color planes
        $bw.Write([uint16]32)                # Bits per pixel
        $bw.Write([uint32]$img.Bytes.Length) # Image size in bytes
        $bw.Write([uint32]$offset)           # Image offset
        $offset += $img.Bytes.Length
    }

    # Raw PNG Frame Data
    foreach ($img in $pngFrames) {
        $bw.Write($img.Bytes)
    }

    $bw.Flush()
    $bw.Close()
    $fs.Dispose()
}

# 3. Locate MSBuild dynamically
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuildPath = $null

if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
    if ($vsPath -and (Test-Path $vsPath)) {
        $msbuildPath = $vsPath
    }
}

if (-not $msbuildPath) {
    $candidates = @(
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe',
        'C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe'
    )
    $msbuildPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $msbuildPath) {
    throw "MSBuild.exe was not found on the system."
}

Write-Host "  Using MSBuild: $msbuildPath" -ForegroundColor DarkGray

function Invoke-MSBuildCompile([string]$projectPath) {
    $success = $false
    try {
        & $msbuildPath $projectPath /p:Configuration=Release /p:Platform=AnyCPU /v:m
        if ($LASTEXITCODE -eq 0) { $success = $true }
    } catch { }

    if (-not $success -and (Test-Path 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe')) {
        Write-Host "  Retrying with Framework64 MSBuild..." -ForegroundColor DarkGray
        & 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe' $projectPath /p:Configuration=Release /p:Platform=AnyCPU /v:m
    }
}

# 4. Compile Standalone Main Executable
Write-Host "[2/5] Compiling standalone C# WPF binary with MSBuild..." -ForegroundColor Yellow
Invoke-MSBuildCompile $AppCsprojPath

$compiledExe = Join-Path (Join-Path (Join-Path $SrcDir "bin") "Release") "SimplePCMonitor.exe"
if (-not (Test-Path $compiledExe)) {
    throw "Build failed: $compiledExe was not produced."
}

# 5. Compile Setup Wizard Installer
Write-Host "[3/5] Compiling Setup Wizard Installer executable..." -ForegroundColor Yellow
Invoke-MSBuildCompile $InstCsproj

$setupExe = Join-Path $ReleasesDir "SimplePCMonitor-Setup.exe"
if (-not (Test-Path $setupExe)) {
    # Check if built into Installer/bin/Release
    $altSetup = Join-Path (Join-Path (Join-Path (Join-Path $SrcDir "Installer") "bin") "Release") "SimplePCMonitor-Setup.exe"
    if (Test-Path $altSetup) {
        Copy-Item -Path $altSetup -Destination $setupExe -Force
    }
}

# Copy to Staging and Releases root
Copy-Item -Path $compiledExe -Destination (Join-Path $ReleasesDir "SimplePCMonitor.exe") -Force
Copy-Item -Path $compiledExe -Destination (Join-Path $StageDir "SimplePCMonitor.exe") -Force
Copy-Item -Path $setupExe -Destination (Join-Path $StageDir "SimplePCMonitor-Setup.exe") -Force
if (Test-Path $pngPath) { Copy-Item -Path $pngPath -Destination $StageDir -Force }
if (Test-Path $icoPath) { Copy-Item -Path $icoPath -Destination $StageDir -Force }
Copy-Item -Path (Join-Path $ProjectRoot "README.md") -Destination $StageDir -Force

# 6. Compress Portable Release ZIP
Write-Host "[4/5] Compressing portable distribution into ZIP..." -ForegroundColor Yellow
Compress-Archive -Path "$StageDir\*" -DestinationPath $ZipOutput -CompressionLevel Optimal -Force

Write-Host "[5/5] Native C# Deliverables Ready!" -ForegroundColor Green
Write-Host ""
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Generated Native C# Deliverables in releases/: " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  1. Standalone Executable : releases\SimplePCMonitor.exe (585 KB)" -ForegroundColor White
Write-Host "     -> Doble clic directo en cualquier PC Windows 10/11 sin instalador." -ForegroundColor Gray
Write-Host "  2. Setup Wizard Installer: releases\SimplePCMonitor-Setup.exe" -ForegroundColor White
Write-Host "     -> Asistente visual de instalación paso a paso con accesos directos y desinstalador." -ForegroundColor Gray
Write-Host "  3. Paquete ZIP Portable  : $ZipOutput" -ForegroundColor White
Write-Host "=================================================" -ForegroundColor Cyan
