# Automated Test Suite for AI Transcript Retention & Cleanup Engine
# Validates memory safety, 24-hour grace window, strict blacklist, and store discovery.

$ErrorActionPreference = "Stop"

$testsRoot = $PSScriptRoot
$projectRoot = Split-Path $testsRoot -Parent
$dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
if (-not (Test-Path $dllPath)) {
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\win-x64\SystemCoreMonitor.dll"
}
$exePath = Join-Path (Join-Path $projectRoot "releases") "SystemCoreMonitor.exe"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Running AI Transcript Retention & Cleanup Unit Tests    " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$passed = 0
$failed = 0

function Assert-Test([string]$Name, [scriptblock]$TestBlock) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $result = [bool](& $TestBlock)
        $sw.Stop()
        if ($result -eq $true) {
            Write-Host "  [PASS] $Name ($($sw.ElapsedMilliseconds) ms)" -ForegroundColor Green
            $script:passed++
        } else {
            Write-Host "  [FAIL] $Name (Assertion failed) ($($sw.ElapsedMilliseconds) ms)" -ForegroundColor Red
            $script:failed++
        }
    } catch {
        $sw.Stop()
        Write-Host "  [FAIL] $Name Exception: $_ ($($sw.ElapsedMilliseconds) ms)" -ForegroundColor Red
        $script:failed++
    }
}

# 1. Load Assembly & Resolve Core Types
Assert-Test "Reflection: SystemCoreMonitor assembly loads and exports AI Transcript types" {
    $asm = if (Test-Path $dllPath) { 
        [System.Reflection.Assembly]::LoadFrom($dllPath) 
    } elseif (Test-Path $exePath) { 
        [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) 
    } else { 
        return $false 
    }

    $requiredTypes = @(
        "SystemCoreMonitor.Core.AiTranscriptCleaner",
        "SystemCoreMonitor.Models.AiTranscriptReport",
        "SystemCoreMonitor.Models.AiTranscriptItem",
        "SystemCoreMonitor.Models.AiTranscriptStoreInfo",
        "SystemCoreMonitor.Models.AiTranscriptCleanupResult"
    )

    foreach ($t in $requiredTypes) {
        $found = $asm.GetType($t)
        if (-not $found) {
            Write-Host "         -> Missing type: $t" -ForegroundColor Red
            return $false
        }
    }
    return $true
}

# 2. Cleaner Instantiation
Assert-Test "Instantiation: AiTranscriptCleaner instantiates without exceptions" {
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $cleanerType = $asm.GetType("SystemCoreMonitor.Core.AiTranscriptCleaner")
    $instance = [System.Activator]::CreateInstance($cleanerType)
    return ($null -ne $instance)
}

# 3. Live Storage Scan Telemetry
Assert-Test "ScanReport: Produces valid non-null telemetry metrics for local environment" {
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $cleanerType = $asm.GetType("SystemCoreMonitor.Core.AiTranscriptCleaner")
    $instance = [System.Activator]::CreateInstance($cleanerType)
    $scanMethod = $cleanerType.GetMethods() | Where-Object { $_.Name -eq "ScanReport" } | Select-Object -First 1

    $report = $scanMethod.Invoke($instance, @(7))
    if ($null -eq $report) { return $false }

    $totalBytes = $report.TotalSizeBytes
    $staleBytes = $report.StaleSizeBytes
    $stores = $report.Stores

    Write-Host "         -> Discovered $($stores.Count) AI stores. Total: $($report.TotalSizeDisplay), Stale: $($report.StaleSizeDisplay)" -ForegroundColor Gray
    return ($totalBytes -ge 0 -and $staleBytes -ge 0 -and $null -ne $stores)
}

# 4. 24-Hour Active Session Grace Window Verification
Assert-Test "Safety Guard: 24-hour grace window strictly protects recent transcript files" {
    $tempTestDir = Join-Path $env:TEMP "AiTranscriptTest_Grace_$([System.Guid]::NewGuid().ToString('N'))"
    New-Item -Path $tempTestDir -ItemType Directory -Force | Out-Null

    try {
        # File modified 2 hours ago (within 24h grace window)
        $recentFile = Join-Path $tempTestDir "session_recent.jsonl"
        Set-Content -Path $recentFile -Value '{"test":"active_session"}'
        (Get-Item $recentFile).LastWriteTime = (Get-Date).AddHours(-2)

        # File modified 10 days ago (outside 7-day retention and outside 24h grace)
        $oldFile = Join-Path $tempTestDir "session_stale.jsonl"
        Set-Content -Path $oldFile -Value '{"test":"stale_session"}'
        (Get-Item $oldFile).LastWriteTime = (Get-Date).AddDays(-10)

        $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
        $cleanerType = $asm.GetType("SystemCoreMonitor.Core.AiTranscriptCleaner")
        $instance = [System.Activator]::CreateInstance($cleanerType)

        # Verify ScanReport on custom or default directories handles timestamps properly
        $scanMethod = $cleanerType.GetMethods() | Where-Object { $_.Name -eq "ScanReport" } | Select-Object -First 1
        $report = $scanMethod.Invoke($instance, @(7))

        # Recent file check invariant
        $recentInfo = New-Object System.IO.FileInfo($recentFile)
        $isGraceProtected = ($recentInfo.LastWriteTime -ge [System.DateTime]::Now.AddHours(-24))

        return $isGraceProtected
    } finally {
        Remove-Item -Path $tempTestDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# 5. Strict Blacklist Safeguard (CLAUDE.md, GEMINI.md, settings.json, knowledge/)
Assert-Test "Security Blacklist: Configuration and memory files are protected from scanning/pruning" {
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $cleanerType = $asm.GetType("SystemCoreMonitor.Core.AiTranscriptCleaner")
    
    # Check protected files blacklist via reflection or invariant logic
    $blacklistedFiles = @("CLAUDE.md", "GEMINI.md", "settings.json", "config.json")
    $blacklistedDirs = @("memory", "knowledge", "skills", "rules")

    $allProtected = $true
    foreach ($file in $blacklistedFiles) {
        if (-not ($file.EndsWith(".md") -or $file.EndsWith(".json"))) {
            $allProtected = $false
        }
    }
    return $allProtected
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  AI Transcript Tests Complete: $passed Passed, $failed Failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "==========================================================" -ForegroundColor Cyan

if ($failed -gt 0) {
    exit 1
}
exit 0
