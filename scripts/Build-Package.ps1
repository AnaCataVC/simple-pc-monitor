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
Write-Host "[2/3] Publishing standalone C# WPF binary with dotnet..." -ForegroundColor Yellow
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

# Copy to Releases root for embedded resource inclusion in Installer
$targetReleaseExe = Join-Path $ReleasesDir "SystemCoreMonitor.exe"
Copy-Item -Path $compiledExe -Destination $targetReleaseExe -Force

# 4. Locate MSBuild and Compile Setup Wizard Installer
Write-Host "[3/3] Compiling Setup Wizard Installer executable..." -ForegroundColor Yellow

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
        'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe',
        'C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe'
    )
    $msbuildPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

$compiledSetup = $false
if ($msbuildPath) {
    try {
        & $msbuildPath $InstCsproj /p:Configuration=Release /p:Platform=AnyCPU /v:m
        if ($LASTEXITCODE -eq 0) { $compiledSetup = $true }
    } catch { }
}

if (-not $compiledSetup -and (Test-Path 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe')) {
    Write-Host "  Retrying with Framework64 MSBuild..." -ForegroundColor DarkGray
    & 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe' $InstCsproj /p:Configuration=Release /p:Platform=AnyCPU /v:m
    if ($LASTEXITCODE -eq 0) { $compiledSetup = $true }
}

$setupExe = Join-Path $ReleasesDir "SystemCoreMonitor-Setup.exe"
if (-not (Test-Path $setupExe)) {
    throw "Build failed: Setup Wizard executable ($setupExe) was not produced."
}

# Clean up temporary staging directory if created
if (Test-Path $StageDir) {
    Remove-Item $StageDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Deliverables Ready!" -ForegroundColor Green
Write-Host ""
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Generated Installer Deliverable in releases/: " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Setup Wizard Installer : $setupExe" -ForegroundColor White
$setupSizeKB = [math]::Round((Get-Item $setupExe).Length / 1KB)
Write-Host "  Setup File Size        : $setupSizeKB KB" -ForegroundColor Gray
Write-Host "=================================================" -ForegroundColor Cyan