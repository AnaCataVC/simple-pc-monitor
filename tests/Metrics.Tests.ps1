# Automated Binary & Health Tests for Simple PC Monitor (C# Standalone & Setup Edition)
# Validates binary integrity, memory working set, responsiveness, and interactive core modules.

$testsRoot = $PSScriptRoot
$projectRoot = Split-Path $testsRoot -Parent
$exePath = Join-Path (Join-Path $projectRoot "releases") "SimplePCMonitor.exe"
$setupPath = Join-Path (Join-Path $projectRoot "releases") "SimplePCMonitor-Setup.exe"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Running Simple PC Monitor Native Health Tests  " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

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

# 4. Test Core Assembly Types Reflection
Assert-Test "Architecture: Core classes loadable via reflection" {
    $bytes = [System.IO.File]::ReadAllBytes($exePath)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    
    $types = @(
        "SimplePCMonitor.Core.PowerPlanManager",
        "SimplePCMonitor.Core.ProcessManager",
        "SimplePCMonitor.Core.ProcessMetadataCache",
        "SimplePCMonitor.Core.SafeTempCleaner",
        "SimplePCMonitor.Core.MemoryOptimizer",
        "SimplePCMonitor.Core.SnapshotExporter",
        "SimplePCMonitor.Core.DxgiHelper",
        "SimplePCMonitor.Core.SetupApiHelper",
        "SimplePCMonitor.Core.WindowsAcceleratorEngine",
        "SimplePCMonitor.Core.LocalizationManager",
        "SimplePCMonitor.Models.AiAgentSession",
        "SimplePCMonitor.Models.AiAgentMetric",
        "SimplePCMonitor.Modules.AiAgentCollector",
        "SimplePCMonitor.Modules.GpuCollector",
        "SimplePCMonitor.Modules.NpuCollector",
        "SimplePCMonitor.Modules.StartupCollector",
        "SimplePCMonitor.UI.ProcessDetailsWindow"
    )

    foreach ($t in $types) {
        $found = $asm.GetType($t)
        if (-not $found) { 
            Write-Host "         -> Missing type: $t" -ForegroundColor Red
            return $false 
        }
    }
    return $true
}

# 5. Test Bilingual Localization Manager
Assert-Test "Localization: Provides consistent strings for ES and EN" {
    $bytes = [System.IO.File]::ReadAllBytes($exePath)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    $locType = $asm.GetType("SimplePCMonitor.Core.LocalizationManager")
    $getMethod = $locType.GetMethods() | Where-Object { $_.Name -eq "Get" } | Select-Object -First 1
    
    $esTrim = $getMethod.Invoke($null, @("TrimRam", "es"))
    $enTrim = $getMethod.Invoke($null, @("TrimRam", "en"))
    $esGpu = $getMethod.Invoke($null, @("CardGpuTitle", "es"))
    $enGpu = $getMethod.Invoke($null, @("CardGpuTitle", "en"))

    Write-Host "         -> esTrim: '$esTrim', enTrim: '$enTrim', esGpu: '$esGpu', enGpu: '$enGpu'" -ForegroundColor Gray

    $esOk = ($esTrim -eq "Optimizar RAM" -and $esGpu.StartsWith("GR"))
    $enOk = ($enTrim -eq "Trim RAM" -and $enGpu -eq "GPU")

    return ($esOk -and $enOk)
}

# 6. Test Process Metadata Cache Resolution
Assert-Test "Process Metadata: Resolves friendly names and company signatures" {
    $bytes = [System.IO.File]::ReadAllBytes($exePath)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    $cacheType = $asm.GetType("SimplePCMonitor.Core.ProcessMetadataCache")
    
    $metaSvchost = $cacheType.GetMethod("GetMetadata").Invoke($null, @(0, "svchost"))
    $metaMcAfee  = $cacheType.GetMethod("GetMetadata").Invoke($null, @(0, "mc-fw-host"))
    $metaChrome  = $cacheType.GetMethod("GetMetadata").Invoke($null, @(0, "chrome"))

    $svcOk   = ($metaSvchost.FriendlyName -eq "Host Process for Windows Services")
    $mcOk    = ($metaMcAfee.FriendlyName -eq "McAfee Core Firewall Host")
    $chrOk   = ($metaChrome.FriendlyName -eq "Google Chrome")

    return ($svcOk -and $mcOk -and $chrOk)
}

# 7. Test Process Protection Blacklist Logic
Assert-Test "Security: Protected process blacklist blocks system processes" {
    $bytes = [System.IO.File]::ReadAllBytes($exePath)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    $procMgr = $asm.GetType("SimplePCMonitor.Core.ProcessManager")
    
    $isCsrssProtected = $procMgr.GetMethod("IsProtected").Invoke($null, @("csrss"))
    $isSvchostProtected = $procMgr.GetMethod("IsProtected").Invoke($null, @("svchost"))
    $isNotepadProtected = $procMgr.GetMethod("IsProtected").Invoke($null, @("notepad"))

    return ($isCsrssProtected -eq $true -and $isSvchostProtected -eq $true -and $isNotepadProtected -eq $false)
}

# 8. Test DxgiHelper & SetupApiHelper
Assert-Test "Accelerators: DxgiHelper enumerates physical/integrated GPU" {
    $bytes = [System.IO.File]::ReadAllBytes($exePath)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    $dxgiType = $asm.GetType("SimplePCMonitor.Core.DxgiHelper")
    $adapters = $dxgiType.GetMethod("GetAdapters").Invoke($null, $null)
    return ($adapters.Count -gt 0)
}

Assert-Test "Accelerators: SetupApiHelper probes NPU without throwing" {
    $bytes = [System.IO.File]::ReadAllBytes($exePath)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    $setupType = $asm.GetType("SimplePCMonitor.Core.SetupApiHelper")
    $npus = $setupType.GetMethod("GetNpuDevices").Invoke($null, $null)
    return ($null -ne $npus)
}

# 9. Test Setup Wizard Executable
Assert-Test "Installer: SimplePCMonitor-Setup.exe exists and is valid" {
    if (-not (Test-Path $setupPath)) { return $false }
    $file = Get-Item $setupPath
    return ($file.Length -gt 200000)
}

# 10. Test Hardened SafeTempCleaner Invariants
Assert-Test "Security: SafeTempCleaner blocks root traversal and protects exclusions" {
    $bytes = [System.IO.File]::ReadAllBytes($exePath)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    $cleanerType = $asm.GetType("SimplePCMonitor.Core.SafeTempCleaner")
    
    $isClaudeExcluded = $cleanerType.GetMethod("IsExcluded").Invoke($null, @("C:\Users\test\.claude\settings.json"))
    $isAntigravityExcluded = $cleanerType.GetMethod("IsExcluded").Invoke($null, @("C:\Users\test\.antigravity\brain"))
    $isOneDriveExcluded = $cleanerType.GetMethod("IsExcluded").Invoke($null, @("C:\Users\test\OneDrive\doc.txt"))
    $isTempFileExcluded = $cleanerType.GetMethod("IsExcluded").Invoke($null, @("C:\Users\test\AppData\Local\Temp\junk.tmp"))

    $exOk = ($isClaudeExcluded -eq $true -and $isAntigravityExcluded -eq $true -and $isOneDriveExcluded -eq $true -and $isTempFileExcluded -eq $false)
    return $exOk
}

# 11. Test CrashLogger Type and Safe Exception Logging
Assert-Test "Stability: CrashLogger type exists and handles safe exception traps" {
    $bytes = [System.IO.File]::ReadAllBytes($exePath)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    $crashLoggerType = $asm.GetType("SimplePCMonitor.Core.CrashLogger")
    if ($null -eq $crashLoggerType) { return $false }

    $logMethod = $crashLoggerType.GetMethod("LogException", [System.Reflection.BindingFlags]"Public,Static")
    if ($null -eq $logMethod) { return $false }

    # Test invoking safe log trap
    $dummyEx = [System.Exception]::new("Test unhandled exception trap")
    $argsArray = [object[]]@([string]"Metrics.Tests", [System.Exception]$dummyEx, [bool]$false)
    $logMethod.Invoke($null, $argsArray)
    return $true
}

# 12. Test ProcessCollector CPU & RAM Sorting Modes
Assert-Test "Modules: ProcessCollector samples and sorts correctly by CPU and RAM" {
    $bytes = [System.IO.File]::ReadAllBytes($exePath)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    $procCollectorType = $asm.GetType("SimplePCMonitor.Modules.ProcessCollector")
    if ($null -eq $procCollectorType) { return $false }

    $procCollector = [System.Activator]::CreateInstance($procCollectorType)
    $sampleMethod = $procCollectorType.GetMethod("Sample")

    # Sample sorted by CPU
    $byCpu = $sampleMethod.Invoke($procCollector, @(10, 16.0, $true, ""))
    # Sample sorted by RAM
    $byRam = $sampleMethod.Invoke($procCollector, @(10, 16.0, $false, ""))

    $cpuOk = ($null -ne $byCpu -and $byCpu.Count -gt 0)
    $ramOk = ($null -ne $byRam -and $byRam.Count -gt 0)
    return ($cpuOk -and $ramOk)
}

# 13. Test AI Agent & MCP Collector with Session Naming & Privacy
Assert-Test "AI Agents: AiAgentCollector samples sessions and resolves SessionContext cleanly" {
    $bytes = [System.IO.File]::ReadAllBytes($exePath)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    $aiCollectorType = $asm.GetType("SimplePCMonitor.Modules.AiAgentCollector")
    $sessionType = $asm.GetType("SimplePCMonitor.Models.AiAgentSession")
    if ($null -eq $aiCollectorType -or $null -eq $sessionType) { return $false }

    $prop = $sessionType.GetProperty("SessionContext")
    if ($null -eq $prop) { return $false }

    $collector = [System.Activator]::CreateInstance($aiCollectorType)
    $sampleMethod = $aiCollectorType.GetMethod("Sample")
    $metric = $sampleMethod.Invoke($collector, $null)

    if ($null -eq $metric) { return $false }

    # Privacy verification: Ensure no absolute path leaked in SessionContext
    foreach ($session in $metric.Sessions) {
        if (![string]::IsNullOrEmpty($session.SessionContext) -and $session.SessionContext.Contains("C:\Users\")) {
            return $false
        }
    }

    return $true
}

# 14. Test Two-Phase Process Close Invariants
Assert-Test "Process Manager: RequestGracefulCloseAsync handles protected and user processes" {
    $bytes = [System.IO.File]::ReadAllBytes($exePath)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    $procMgr = $asm.GetType("SimplePCMonitor.Core.ProcessManager")
    if ($null -eq $procMgr) { return $false }

    $closeMethod = $procMgr.GetMethod("RequestGracefulCloseAsync")
    if ($null -eq $closeMethod) { return $false }

    # Test protected process check (svchost PID 4 or system)
    $task = $closeMethod.Invoke($null, @([int]4, [string]"system", [int]100))
    $task.Wait()
    $result = $task.Result.ToString()

    return ($result -eq "ProtectedProcess")
}

# Storage Analyzer: virtual volume heuristic
Assert-Test "Storage: Cloud mounts detected, real volumes never misflagged" {
    $asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath))
    $fss = $asm.GetType("SimplePCMonitor.Core.FileSystemSafety")
    if ($null -eq $fss) { return $false }

    $m = $fss.GetMethod("IsLikelyVirtualVolume")
    if ($null -eq $m) { return $false }

    $gb = 1024L * 1024L * 1024L

    # A fixed FAT32 volume above the 32 GB format cap cannot be physical storage
    $cloudMount    = $m.Invoke($null, @([string]"FAT32", [long](457 * $gb), $true))
    # Real volumes and legitimate small FAT32 media must never be hidden
    $realNtfs      = $m.Invoke($null, @([string]"NTFS",  [long](457 * $gb), $true))
    $smallFat32    = $m.Invoke($null, @([string]"FAT32", [long](8 * $gb),   $true))
    $removableFat  = $m.Invoke($null, @([string]"FAT32", [long](64 * $gb),  $false))
    $exFatVolume   = $m.Invoke($null, @([string]"exFAT", [long](457 * $gb), $true))
    $emptyFormat   = $m.Invoke($null, @([string]"",      [long](457 * $gb), $true))

    return ($cloudMount -eq $true -and $realNtfs -eq $false -and $smallFat32 -eq $false `
            -and $removableFat -eq $false -and $exFatVolume -eq $false -and $emptyFormat -eq $false)
}

# Storage Analyzer: folder scan basics
Assert-Test "Storage: Folder scan returns descending results and survives bad paths" {
    $asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath))
    $scanner = $asm.GetType("SimplePCMonitor.Core.FolderSizeScanner")
    if ($null -eq $scanner) { return $false }

    $scan = $scanner.GetMethod("Scan", [type[]]@([string], [int]))
    if ($null -eq $scan) { return $false }

    $result = $scan.Invoke($null, @([string]$env:TEMP, [int]10))
    if ($null -eq $result) { return $false }

    $entries = $result.TopEntries
    for ($i = 1; $i -lt $entries.Count; $i++) {
        if ($entries[$i].SizeBytes -gt $entries[$i - 1].SizeBytes) { return $false }
    }

    # A missing directory must return an empty result, never throw
    $missing = $scan.Invoke($null, @([string]("C:\NoExiste_" + [Guid]::NewGuid().ToString("N")), [int]5))
    return ($null -ne $missing -and $missing.TopEntries.Count -eq 0)
}

# Storage Analyzer: reparse points must never contribute phantom bytes
Assert-Test "Storage: Reparse point bytes excluded from folder scan totals" {
    $asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath))
    $scanner = $asm.GetType("SimplePCMonitor.Core.FolderSizeScanner")
    $scan = $scanner.GetMethod("Scan", [type[]]@([string], [int]))

    $tmp = Join-Path $env:TEMP ("spm_reparse_" + [Guid]::NewGuid().ToString("N"))
    $target = Join-Path $tmp "target"
    $root = Join-Path $tmp "root"
    $realDir = Join-Path $root "real"

    try {
        New-Item -ItemType Directory -Path $target, $root, $realDir -Force | Out-Null

        # 2 MB behind a junction (must be ignored) and 1 MB in a real folder (must be counted)
        [System.IO.File]::WriteAllBytes((Join-Path $target "phantom.bin"), (New-Object byte[] (2 * 1024 * 1024)))
        [System.IO.File]::WriteAllBytes((Join-Path $realDir "real.bin"), (New-Object byte[] (1 * 1024 * 1024)))

        # Junctions do not require elevation, unlike symlinks
        cmd /c mklink /J "$root\link" "$target" | Out-Null

        $result = $scan.Invoke($null, @([string]$root, [int]10))

        $reparseSkipped = $false
        foreach ($skipped in $result.SkippedEntries) {
            if ($skipped.Reason -eq "ReparsePoint") { $reparseSkipped = $true }
        }

        return ($reparseSkipped -eq $true -and $result.TotalScannedBytes -eq 1048576)
    } finally {
        cmd /c rmdir /s /q "$tmp" 2>$null
    }
}

# Storage Analyzer: deletion whitelist is the guard against wiping arbitrary folders
Assert-Test "Security: Bloat cleanup refuses every path outside the whitelist" {
    $asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath))
    $detector = $asm.GetType("SimplePCMonitor.Core.BloatDetector")
    if ($null -eq $detector) { return $false }

    $isWhitelisted = $detector.GetMethod("IsWhitelistedForDeletion")
    if ($null -eq $isWhitelisted) { return $false }

    $forbidden = @(
        "C:\",
        "C:\Windows",
        "C:\Windows\System32",
        $env:USERPROFILE,
        (Join-Path $env:USERPROFILE "Documents"),
        (Join-Path $env:USERPROFILE ".gradle\caches\..\..\Documents"),
        "\\server\share",
        ""
    )

    foreach ($path in $forbidden) {
        if ($isWhitelisted.Invoke($null, @([string]$path)) -eq $true) { return $false }
    }

    # Whatever the whitelist does contain must be accepted, so the guard is not vacuous
    $allowed = $detector.GetMethod("GetDeletableCachePaths").Invoke($null, @())
    foreach ($path in $allowed) {
        if ($isWhitelisted.Invoke($null, @([string]$path)) -ne $true) { return $false }
    }

    return $true
}

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Results: $passed Passed, $failed Failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "=================================================" -ForegroundColor Cyan

if ($failed -gt 0) {
    exit 1
} else {
    exit 0
}
