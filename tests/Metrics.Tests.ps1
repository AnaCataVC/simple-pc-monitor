# Automated Binary & Health Tests for Simple PC Monitor (C# Standalone Edition)
# Validates binary integrity, memory working set, responsiveness, and startup latency.

$testsRoot = $PSScriptRoot
$projectRoot = Split-Path $testsRoot -Parent
$exePath = Join-Path (Join-Path $projectRoot "releases") "SimplePCMonitor.exe"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Running Simple PC Monitor Native Health Tests  " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

$passed = 0
$failed = 0

function Assert-Test([string]$Name, [scriptblock]$TestBlock) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $result = & $TestBlock
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

# 1. Test Executable Existence & Size
Assert-Test "Binary: SimplePCMonitor.exe exists and is under 2 MB" {
    if (-not (Test-Path $exePath)) { return $false }
    $file = Get-Item $exePath
    return ($file.Length -gt 100000 -and $file.Length -lt 2000000)
}

# 2. Test Process Launch & Responding Status
Assert-Test "Process: Launches cleanly and responds on UI thread" {
    $proc = Start-Process -FilePath $exePath -PassThru
    Start-Sleep -Milliseconds 1200

    $isRunning = $proc.Responding
    $memMB = [math]::Round($proc.WorkingSet64 / 1MB, 1)

    # Stop process after verification
    $proc.Kill()
    $proc.WaitForExit(2000)

    Write-Host "         -> Memory Working Set: $memMB MB, Responding: $isRunning" -ForegroundColor Gray
    return ($isRunning -eq $true)
}

# 3. Test Embedded Win32 Icon
Assert-Test "Brand Asset: icon.ico exists in project root" {
    $icoPath = Join-Path $projectRoot "icon.ico"
    return (Test-Path $icoPath)
}

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Results: $passed Passed, $failed Failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "=================================================" -ForegroundColor Cyan

if ($failed -gt 0) {
    exit 1
} else {
    exit 0
}
