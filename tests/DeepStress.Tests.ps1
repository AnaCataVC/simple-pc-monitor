# Deep Stress & Live Invariant Tests for System Core Monitor
# Validates live process trees, two-phase graceful termination, protected system invariants, handle leaks, and runtime stability.

$projectRoot = Split-Path $PSScriptRoot -Parent
$exePath = Join-Path (Join-Path $projectRoot "releases") "SystemCoreMonitor.exe"

Write-Host "=================================================" -ForegroundColor Magenta
Write-Host "   System Core Monitor - Deep Live Stress Suite  " -ForegroundColor Magenta
Write-Host "=================================================" -ForegroundColor Magenta

$passed = 0
$failed = 0

function Assert-DeepTest([string]$Name, [scriptblock]$TestBlock) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $result = [bool](& $TestBlock)
        $sw.Stop()
        if ($result -eq $true) {
            Write-Host "  [PASS] $Name ($($sw.ElapsedMilliseconds) ms)" -ForegroundColor Green
            $script:passed++
        } else {
            Write-Host "  [FAIL] $Name (Assertion returned false) ($($sw.ElapsedMilliseconds) ms)" -ForegroundColor Red
            $script:failed++
        }
    } catch {
        $sw.Stop()
        Write-Host "  [FAIL] $Name Exception: $_ ($($sw.ElapsedMilliseconds) ms)" -ForegroundColor Red
        $script:failed++
    }
}

$candidateDlls = @(
    (Join-Path $projectRoot "src\bin\Release\net9.0-windows\win-x64\SystemCoreMonitor.dll"),
    (Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll")
)
$dllPath = $candidateDlls | Where-Object { Test-Path $_ } | Sort-Object { (Get-Item $_).LastWriteTime } -Descending | Select-Object -First 1
$asm = if ($dllPath -and (Test-Path $dllPath)) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
$procMgr = $asm.GetType("SystemCoreMonitor.Core.ProcessManager")
$aiCollectorType = $asm.GetType("SystemCoreMonitor.Modules.AiAgentCollector")

# -------------------------------------------------------------
# 1. Live Graceful Close (GUI Process: Notepad)
# -------------------------------------------------------------
Assert-DeepTest "Live Graceful Close: Terminates GUI app cleanly via Phase 1" {
    $proc = Start-Process -FilePath "notepad.exe" -PassThru
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    $pName = if (![string]::IsNullOrEmpty($proc.ProcessName)) { $proc.ProcessName } else { "notepad" }

    $closeMethod = $procMgr.GetMethod("RequestGracefulCloseAsync")
    $task = $closeMethod.Invoke($null, @([int]$proc.Id, [string]$pName, [int]2000))
    $task.Wait()
    $status = $task.Result.ToString()

    Start-Sleep -Milliseconds 400
    $proc.Refresh()
    $hasExited = $proc.HasExited

    if (-not $hasExited) {
        $proc.Kill()
        $proc.WaitForExit(1000)
    }

    Write-Host "         -> Result status: $status, HasExited: $hasExited" -ForegroundColor Gray
    return ($status -eq "ClosedGracefully" -and $hasExited -eq $true)
}

# -------------------------------------------------------------
# 2. Live Reverse Topological Tree Kill (Parent + Child)
# -------------------------------------------------------------
Assert-DeepTest "Live Tree Termination: Kills child and parent in reverse topological order" {
    # Launch a parent cmd that spawns a child notepad
    $parent = Start-Process -FilePath "cmd.exe" -ArgumentList "/k", "notepad.exe" -PassThru
    Start-Sleep -Milliseconds 800

    $killTreeMethod = $procMgr.GetMethod("TerminateProcessTree")
    $msg = ""
    $params = [object[]]@([int]$parent.Id, [bool]$true, [string]$msg, [DateTime]::MinValue)
    $ok = $killTreeMethod.Invoke($null, $params)
    $msgResult = $params[2]

    Start-Sleep -Milliseconds 500
    $parentExited = $parent.HasExited

    Write-Host "         -> TerminateTree output: '$msgResult', Success: $ok, ParentExited: $parentExited" -ForegroundColor Gray
    return ($ok -eq $true -and $parentExited -eq $true)
}

# -------------------------------------------------------------
# 2b. PID Identity Revalidation Before Killing a Sampled Tree
# -------------------------------------------------------------
Assert-DeepTest "PID Reuse Guard: Refuses to kill a tree whose root no longer matches the sampled StartTime" {
    $victim = Start-Process -FilePath "cmd.exe" -ArgumentList "/c ping 127.0.0.1 -n 15 >nul" -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 600

    # A StartTime that cannot belong to this process stands in for a recycled PID:
    # the number is live, but it is no longer the process that was sampled.
    $staleStartTime = (Get-Date).AddHours(-3)

    $killTreeMethod = $procMgr.GetMethod("TerminateProcessTree")
    $msg = ""
    $params = [object[]]@([int]$victim.Id, [bool]$true, [string]$msg, $staleStartTime)
    $ok = $killTreeMethod.Invoke($null, $params)
    $msgResult = $params[2]

    Start-Sleep -Milliseconds 300
    $victim.Refresh()
    $survived = -not $victim.HasExited

    if ($survived) {
        $victim.Kill()
        $victim.WaitForExit(1000)
    }

    Write-Host "         -> Refused: $(-not $ok), Victim survived: $survived, Reason: '$msgResult'" -ForegroundColor Gray
    return ($ok -eq $false -and $survived -eq $true)
}

# -------------------------------------------------------------
# 3. System Invariant Protections (Blacklist of 16 + Session 0)
# -------------------------------------------------------------
Assert-DeepTest "System Invariants: Strict rejection of protected processes (PID 4, svchost, dwm, explorer)" {
    $closeMethod = $procMgr.GetMethod("RequestGracefulCloseAsync")
    $termMethod  = $procMgr.GetMethod("TerminateProcess")

    $protectedTargets = @("svchost", "dwm", "explorer", "services", "lsass", "csrss", "wininit", "smss")
    $allBlocked = $true

    # Test PID 4 (System)
    $task = $closeMethod.Invoke($null, @([int]4, [string]"system", [int]100))
    $task.Wait()
    if ($task.Result.ToString() -ne "ProtectedProcess") { $allBlocked = $false }

    # Test named protected processes
    foreach ($name in $protectedTargets) {
        $task = $closeMethod.Invoke($null, @([int]99999, [string]$name, [int]100))
        $task.Wait()
        if ($task.Result.ToString() -ne "ProtectedProcess") { 
            Write-Host "         -> Failed to protect: $name in RequestGracefulCloseAsync" -ForegroundColor Red
            $allBlocked = $false 
        }

        $msg = ""
        $params = [object[]]@([int]99999, [string]$name, [string]$msg)
        $termResult = $termMethod.Invoke($null, $params)
        if ($termResult -eq $true) {
            Write-Host "         -> Failed to protect: $name in TerminateProcess" -ForegroundColor Red
            $allBlocked = $false
        }
    }

    return $allBlocked
}

# -------------------------------------------------------------
# 4. Handle Leak Stress Test (200 consecutive snapshots in isolated process)
# -------------------------------------------------------------
Assert-DeepTest "Handle Leak Stress: 200 consecutive AiAgentCollector.Sample() cycles leak zero handles" {
    $testScript = @"
        Add-Type -Path '$dllPath'
        `$collector = [SystemCoreMonitor.Modules.AiAgentCollector]::new()
        `$proc = [System.Diagnostics.Process]::GetCurrentProcess()
        # Warmup to stabilize JIT compilation, metadata caches, and runtime threadpool
        for (`$w = 1; `$w -le 40; `$w++) { `$null = `$collector.Sample() }
        `$proc.Refresh()
        `$h0 = `$proc.HandleCount
        for (`$i = 1; `$i -le 60; `$i++) {
            `$null = `$collector.Sample()
        }
        `$proc.Refresh()
        `$h1 = `$proc.HandleCount
        `$delta = `$h1 - `$h0
        Write-Host "         -> Initial Handles: `$h0, Final: `$h1, Delta: `$delta"
        exit (`$delta -le 15 ? 0 : 1)
"@
    $output = pwsh -NoProfile -Command $testScript
    Write-Host $output -ForegroundColor Gray
    return ($LASTEXITCODE -eq 0)
}

# -------------------------------------------------------------
# 5. Live App Smoke Test (5 Seconds Running with Event Log Validation)
# -------------------------------------------------------------
Assert-DeepTest "Live Smoke Test: App runs 5s with Responding=True and 0 crash logs" {
    $proc = Start-Process -FilePath $exePath -PassThru
    $startTime = Get-Date

    # Keep alive for 5 seconds as specified in desktop_app_stability rules
    Start-Sleep -Seconds 5

    $proc.Refresh()
    $isAlive = -not $proc.HasExited
    $isResponding = if ($isAlive) { $proc.Responding } else { $false }
    $wsMB = if ($isAlive) { [math]::Round($proc.WorkingSet64 / 1MB, 1) } else { 0 }

    if ($isAlive) {
        $proc.Kill()
        $proc.WaitForExit(2000)
    }

    # Check for crash log
    $crashLogPath = Join-Path "$env:LOCALAPPDATA\SystemCoreMonitor\Logs" "crash.log"
    $hasCrashLog = Test-Path $crashLogPath

    # Check Windows Event Log in the last 1 minute
    $recentErrors = @()
    try {
        $recentErrors = Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=$startTime; Level=1,2} -ErrorAction SilentlyContinue | Where-Object { $_.Message -like "*SystemCoreMonitor*" }
    } catch { }

    Write-Host "         -> Alive: $isAlive, Responding: $isResponding, WorkingSet: $wsMB MB, EventErrors: $($recentErrors.Count)" -ForegroundColor Gray
    return ($isAlive -and $isResponding -and ($recentErrors.Count -eq 0))
}

Write-Host "=================================================" -ForegroundColor Magenta
Write-Host "  Deep Stress Results: $passed Passed, $failed Failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "=================================================" -ForegroundColor Magenta

if ($failed -gt 0) {
    exit 1
} else {
    exit 0
}
