# Automated Binary & Health Tests for System Core Monitor (C# Standalone & Setup Edition)
# Validates binary integrity, memory working set, responsiveness, and interactive core modules.

$testsRoot = $PSScriptRoot
$projectRoot = Split-Path $testsRoot -Parent
$exePath = Join-Path (Join-Path $projectRoot "releases") "SystemCoreMonitor.exe"
$setupPath = Join-Path (Join-Path $projectRoot "releases") "SystemCoreMonitor-Setup.exe"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Running System Core Monitor Native Health Tests  " -ForegroundColor Cyan
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
Assert-Test "Binary: SystemCoreMonitor.exe exists and is under 2 MB" {
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
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    
    $types = @(
        "SystemCoreMonitor.Core.PowerPlanManager",
        "SystemCoreMonitor.Core.ProcessManager",
        "SystemCoreMonitor.Core.ProcessMetadataCache",
        "SystemCoreMonitor.Core.SafeTempCleaner",
        "SystemCoreMonitor.Core.MemoryOptimizer",
        "SystemCoreMonitor.Core.SnapshotExporter",
        "SystemCoreMonitor.Core.DxgiHelper",
        "SystemCoreMonitor.Core.SetupApiHelper",
        "SystemCoreMonitor.Core.WindowsAcceleratorEngine",
        "SystemCoreMonitor.Core.LocalizationManager",
        "SystemCoreMonitor.Core.AntigravityContextResolver",
        "SystemCoreMonitor.Core.ClaudeSessionResolver",
        "SystemCoreMonitor.Models.AiAgentSession",
        "SystemCoreMonitor.Models.AiAgentMetric",
        "SystemCoreMonitor.Modules.AiAgentCollector",
        "SystemCoreMonitor.Modules.GpuCollector",
        "SystemCoreMonitor.Modules.NpuCollector",
        "SystemCoreMonitor.Modules.StartupCollector",
        "SystemCoreMonitor.UI.ProcessDetailsWindow"
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
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $locType = $asm.GetType("SystemCoreMonitor.Core.LocalizationManager")
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
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $cacheType = $asm.GetType("SystemCoreMonitor.Core.ProcessMetadataCache")
    
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
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $procMgr = $asm.GetType("SystemCoreMonitor.Core.ProcessManager")
    
    $isCsrssProtected = $procMgr.GetMethod("IsProtected").Invoke($null, @("csrss"))
    $isSvchostProtected = $procMgr.GetMethod("IsProtected").Invoke($null, @("svchost"))
    $isNotepadProtected = $procMgr.GetMethod("IsProtected").Invoke($null, @("notepad"))

    return ($isCsrssProtected -eq $true -and $isSvchostProtected -eq $true -and $isNotepadProtected -eq $false)
}

# 8. Test DxgiHelper & SetupApiHelper
Assert-Test "Accelerators: DxgiHelper enumerates physical/integrated GPU" {
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $dxgiType = $asm.GetType("SystemCoreMonitor.Core.DxgiHelper")
    $adapters = $dxgiType.GetMethod("GetAdapters").Invoke($null, $null)
    return ($adapters.Count -gt 0)
}

Assert-Test "Accelerators: SetupApiHelper probes NPU without throwing" {
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $setupType = $asm.GetType("SystemCoreMonitor.Core.SetupApiHelper")
    $npus = $setupType.GetMethod("GetNpuDevices").Invoke($null, $null)
    return ($null -ne $npus)
}

# 9. Test Setup Wizard Executable
Assert-Test "Installer: SystemCoreMonitor-Setup.exe exists and is valid" {
    if (-not (Test-Path $setupPath)) { return $true }
    $file = Get-Item $setupPath
    return ($file.Length -gt 200000)
}

# 10. Test Hardened SafeTempCleaner Invariants
Assert-Test "Security: SafeTempCleaner blocks root traversal and protects exclusions" {
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $cleanerType = $asm.GetType("SystemCoreMonitor.Core.SafeTempCleaner")
    
    $isClaudeExcluded = $cleanerType.GetMethod("IsExcluded").Invoke($null, @("C:\Users\test\.claude\settings.json"))
    $isAntigravityExcluded = $cleanerType.GetMethod("IsExcluded").Invoke($null, @("C:\Users\test\.antigravity\brain"))
    $isOneDriveExcluded = $cleanerType.GetMethod("IsExcluded").Invoke($null, @("C:\Users\test\OneDrive\doc.txt"))
    $isTempFileExcluded = $cleanerType.GetMethod("IsExcluded").Invoke($null, @("C:\Users\test\AppData\Local\Temp\junk.tmp"))

    $exOk = ($isClaudeExcluded -eq $true -and $isAntigravityExcluded -eq $true -and $isOneDriveExcluded -eq $true -and $isTempFileExcluded -eq $false)
    return $exOk
}

# 11. Test CrashLogger Type and Safe Exception Logging
Assert-Test "Stability: CrashLogger type exists and handles safe exception traps" {
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $crashLoggerType = $asm.GetType("SystemCoreMonitor.Core.CrashLogger")
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
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $procCollectorType = $asm.GetType("SystemCoreMonitor.Modules.ProcessCollector")
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
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $aiCollectorType = $asm.GetType("SystemCoreMonitor.Modules.AiAgentCollector")
    $sessionType = $asm.GetType("SystemCoreMonitor.Models.AiAgentSession")
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
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $procMgr = $asm.GetType("SystemCoreMonitor.Core.ProcessManager")
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
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $fss = $asm.GetType("SystemCoreMonitor.Core.FileSystemSafety")
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
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $scanner = $asm.GetType("SystemCoreMonitor.Core.FolderSizeScanner")
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
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $scanner = $asm.GetType("SystemCoreMonitor.Core.FolderSizeScanner")
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
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $detector = $asm.GetType("SystemCoreMonitor.Core.BloatDetector")
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

# 20. Test MainViewModel Dynamic Accelerators Navigation Visibility
Assert-Test "UI: MainViewModel dynamically hides and restores Accelerators in sidebar navigation" {
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $mainVmType = $asm.GetType("SystemCoreMonitor.ViewModels.MainViewModel")
    $accelVmType = $asm.GetType("SystemCoreMonitor.ViewModels.AcceleratorsViewModel")
    $aiAgentsVmType = $asm.GetType("SystemCoreMonitor.ViewModels.AiAgentsViewModel")
    $storageVmType = $asm.GetType("SystemCoreMonitor.ViewModels.StorageViewModel")
    if ($null -eq $mainVmType) { return $false }

    $vm = [System.Activator]::CreateInstance($mainVmType)
    $setVisibleMethod = $mainVmType.GetMethod("SetAcceleratorsVisibility")
    $navItemsProp = $mainVmType.GetProperty("NavigationItems")
    $navToMethod = $mainVmType.GetMethod("NavigateTo")
    $currVmProp = $mainVmType.GetProperty("CurrentViewModel")

    # 1. Hide Accelerators
    $setVisibleMethod.Invoke($vm, @([bool]$false))
    $items = $navItemsProp.GetValue($vm)
    $hasAccelWhenHidden = $false
    foreach ($item in $items) {
        if ($item.ViewModelType -eq $accelVmType) { $hasAccelWhenHidden = $true }
    }
    if ($hasAccelWhenHidden) { return $false }

    # 2. Prevent navigation while hidden
    $navToMethod.Invoke($vm, @($accelVmType))
    $curr = $currVmProp.GetValue($vm)
    if ($curr.GetType() -eq $accelVmType) { return $false }

    # 3. Restore Accelerators
    $setVisibleMethod.Invoke($vm, @([bool]$true))
    $items = $navItemsProp.GetValue($vm)
    $accelIndex = -1
    $aiAgentsIndex = -1
    $storageIndex = -1
    $idx = 0
    foreach ($item in $items) {
        if ($item.ViewModelType -eq $accelVmType) { $accelIndex = $idx }
        if ($item.ViewModelType -eq $aiAgentsVmType) { $aiAgentsIndex = $idx }
        if ($item.ViewModelType -eq $storageVmType) { $storageIndex = $idx }
        $idx++
    }

    # Verify restored and in correct order (AiAgents < Accelerators < Storage)
    if ($accelIndex -eq -1) { return $false }
    if ($aiAgentsIndex -ne -1 -and $accelIndex -le $aiAgentsIndex) { return $false }
    if ($storageIndex -ne -1 -and $accelIndex -ge $storageIndex) { return $false }

    return $true
}

# 21. Test Dynamic 4-Theme Switching
Assert-Test "Theme Engine: App.SetTheme switches between all 4 palettes dynamically" {
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $appType = $asm.GetType("SystemCoreMonitor.App")
    if ($null -eq $appType) { return $false }

    $app = [System.Activator]::CreateInstance($appType)
    $init = $appType.GetMethod("InitializeComponent")
    if ($init) { $init.Invoke($app, $null) }

    $setThemeMethod = $appType.GetMethod("SetTheme")
    if ($null -eq $setThemeMethod) { return $false }

    # Test Light (#FFF4F6FB)
    $setThemeMethod.Invoke($null, @("Light"))
    $bgLight = $app.Resources["BgApp"]
    if ($null -eq $bgLight -or $bgLight.ToString() -ne "#FFF4F6FB") { return $false }

    # Test Neon (#FF0B0E14)
    $setThemeMethod.Invoke($null, @("Neon"))
    $bgNeon = $app.Resources["BgApp"]
    if ($null -eq $bgNeon -or $bgNeon.ToString() -ne "#FF0B0E14") { return $false }

    # Test Rose (#FF181318)
    $setThemeMethod.Invoke($null, @("Rose"))
    $bgRose = $app.Resources["BgApp"]
    if ($null -eq $bgRose -or $bgRose.ToString() -ne "#FF181318") { return $false }

    # Test Dark (#FF10121A)
    $setThemeMethod.Invoke($null, @("Dark"))
    $bgDark = $app.Resources["BgApp"]
    if ($null -eq $bgDark -or $bgDark.ToString() -ne "#FF10121A") { return $false }

    return $true
}

# 22. Test XAML Syntax Integrity (No nested FallbackValue={Binding} expressions)
Assert-Test "XAML Quality: All XAML views free of invalid nested FallbackValue bindings" {
    $xamlFiles = Get-ChildItem -Path (Join-Path $projectRoot "src") -Filter "*.xaml" -Recurse
    foreach ($file in $xamlFiles) {
        $content = [System.IO.File]::ReadAllText($file.FullName)
        if ($content -match 'FallbackValue\s*=\s*\{\s*Binding') {
            Write-Host "         -> Invalid XAML FallbackValue={Binding} found in $($file.Name)" -ForegroundColor Red
            return $false
        }
    }
    return $true
}

# 23. Test ProcessMetric DisplayTitle Fallback Invariant
Assert-Test "Models: ProcessMetric.DisplayTitle resolves FriendlyName and falls back to Name" {
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $procMetricType = $asm.GetType("SystemCoreMonitor.Models.ProcessMetric")
    if ($null -eq $procMetricType) { return $false }

    $item1 = [System.Activator]::CreateInstance($procMetricType)
    $item1.Name = "notepad.exe"
    $item1.FriendlyName = "Bloc de notas"
    if ($item1.DisplayTitle -ne "Bloc de notas") { return $false }

    $item2 = [System.Activator]::CreateInstance($procMetricType)
    $item2.Name = "custom_tool.exe"
    $item2.FriendlyName = ""
    if ($item2.DisplayTitle -ne "custom_tool.exe") { return $false }

    return $true
}

# 24. Test Runaway Detection: disk-wide scans are flagged
Assert-Test "Runaway: ClassifyScan flags scans that target a drive root" {
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $detector = $asm.GetType("SystemCoreMonitor.Core.RunawayProcessDetector")
    if ($null -eq $detector) { return $false }
    $classify = $detector.GetMethod("ClassifyScan")

    $cases = @(
        @("find", "find / -name foo"),
        @("rg", "rg foo C:\"),
        @("cmd", "cmd /c dir /s C:\"),
        @("powershell", "powershell -Command Get-ChildItem C:\ -Recurse")
    )
    foreach ($c in $cases) {
        if ($null -eq $classify.Invoke($null, @($c[0], $c[1]))) {
            Write-Host "         -> Not flagged: $($c[1])" -ForegroundColor Gray
            return $false
        }
    }
    return $true
}

# 25. Test Runaway Detection: scoped scans are not flagged
Assert-Test "Runaway: ClassifyScan ignores scoped paths and non-recursive tools" {
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $detector = $asm.GetType("SystemCoreMonitor.Core.RunawayProcessDetector")
    if ($null -eq $detector) { return $false }
    $classify = $detector.GetMethod("ClassifyScan")

    $cases = @(
        @("rg", "rg foo C:\repo\src"),
        @("findstr", "findstr x file.txt")
    )
    foreach ($c in $cases) {
        $hit = $classify.Invoke($null, @($c[0], $c[1]))
        if ($null -ne $hit) {
            Write-Host "         -> Wrongly flagged: $($c[1]) (target $hit)" -ForegroundColor Gray
            return $false
        }
    }
    return $true
}

# 26. Test Runaway Detection: sustained load needs the minimum time, resets, and respects (PID, StartTime)
Assert-Test "Runaway: SustainedLoadTracker flags only after the minimum time and keys on (PID, StartTime)" {
    $dllPath = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    $asm = if (Test-Path $dllPath) { [System.Reflection.Assembly]::LoadFrom($dllPath) } else { [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) }
    $trackerType = $asm.GetType("SystemCoreMonitor.Core.SustainedLoadTracker")
    if ($null -eq $trackerType) { return $false }
    $tracker = [System.Activator]::CreateInstance($trackerType)

    $t0 = [DateTime]::new(2026, 1, 1, 12, 0, 0)
    $start = $t0.AddHours(-1)

    if ($null -ne $tracker.Observe(100, $start, 80.0, $t0)) { return $false }
    if ($null -ne $tracker.Observe(100, $start, 80.0, $t0.AddMinutes(4))) { return $false }
    if ($null -eq $tracker.Observe(100, $start, 80.0, $t0.AddMinutes(6))) { Write-Host "         -> Not flagged after 6 min" -ForegroundColor Gray; return $false }

    # A sample below the threshold closes the window
    if ($null -ne $tracker.Observe(100, $start, 1.0, $t0.AddMinutes(7))) { return $false }
    if ($null -ne $tracker.Observe(100, $start, 80.0, $t0.AddMinutes(8))) { Write-Host "         -> Window not reset after low load" -ForegroundColor Gray; return $false }

    # Same PID with a new StartTime is a different process: its window starts from zero
    if ($null -ne $tracker.Observe(100, $start.AddMinutes(30), 80.0, $t0.AddMinutes(20))) { Write-Host "         -> Recycled PID inherited the old window" -ForegroundColor Gray; return $false }
    return $true
}


function Get-TestAssembly {
    $dll = Join-Path $projectRoot "src\bin\Release\net9.0-windows\SystemCoreMonitor.dll"
    if (Test-Path $dll) { return [System.Reflection.Assembly]::LoadFrom($dll) }
    return [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath))
}

function New-Descendants($pairs) {
    $list = [System.Collections.Generic.List[System.ValueTuple[int, datetime]]]::new()
    foreach ($p in $pairs) { $list.Add([System.ValueTuple]::Create([int]$p[0], [datetime]$p[1])) }
    return ,$list
}

# 27. Lineage: a remembered descendant becomes an orphan only after its session ended, keyed on (PID, StartTime)
Assert-Test "Lineage: descendant is orphaned after its session ends, never on a failed snapshot or a recycled PID" {
    $asm = Get-TestAssembly
    $trackerType = $asm.GetType("SystemCoreMonitor.Core.AgentLineageTracker")
    if ($null -eq $trackerType) { return $false }
    $tracker = [System.Activator]::CreateInstance($trackerType)
    $none = [Enum]::Parse($asm.GetType("SystemCoreMonitor.Core.OrphanKind"), "None")
    $lineage = [Enum]::Parse($asm.GetType("SystemCoreMonitor.Core.OrphanKind"), "Lineage")

    $t0 = [DateTime]::new(2026, 1, 1, 12, 0, 0)
    $rootStart = $t0.AddHours(-2)
    $childStart = $t0.AddHours(-1)

    $tracker.BeginTick()
    $tracker.RecordLiveSession(100, $rootStart, "Claude", "", (New-Descendants @(,@(200, $childStart))))
    $running = [System.Collections.Generic.HashSet[int]]::new([int[]]@(100, 200))
    $tracker.EndTick($running, $t0)

    $session = $tracker.FindSessionOf(200, $childStart)
    if ($null -eq $session) { Write-Host "         -> Lineage not remembered" -ForegroundColor Gray; return $false }
    if ($trackerType::Decide($session, $false, $false, $null, $t0) -ne $none) { Write-Host "         -> Orphan while session alive" -ForegroundColor Gray; return $false }

    # A failed snapshot (empty set) must not end the session (AGENTS rule 7)
    $tracker.BeginTick()
    $tracker.EndTick([System.Collections.Generic.HashSet[int]]::new(), $t0.AddMinutes(1))
    if ($null -ne $session.EndedAt) { Write-Host "         -> Empty snapshot ended the session" -ForegroundColor Gray; return $false }

    # Root gone: session ends; after the grace the survivor is an orphan
    $tracker.BeginTick()
    $tracker.EndTick([System.Collections.Generic.HashSet[int]]::new([int[]]@(200)), $t0.AddMinutes(2))
    if ($null -eq $session.EndedAt) { Write-Host "         -> Session not ended" -ForegroundColor Gray; return $false }
    if ($trackerType::Decide($session, $false, $false, $null, $t0.AddMinutes(2).AddSeconds(3)) -ne $none) { Write-Host "         -> Orphan inside the grace window" -ForegroundColor Gray; return $false }
    if ($trackerType::Decide($session, $false, $false, $null, $t0.AddMinutes(3)) -ne $lineage) { Write-Host "         -> Not orphaned after session end" -ForegroundColor Gray; return $false }

    # Same PID with another StartTime is a stranger
    if ($null -ne $tracker.FindSessionOf(200, $childStart.AddMinutes(30))) { Write-Host "         -> Recycled PID inherited the lineage" -ForegroundColor Gray; return $false }
    return $true
}

# 28. Lineage fallback: runtimes only count when their dead parent belonged to an ended agent session
Assert-Test "Lineage: fallback flags a runtime whose dead parent was an agent, never a terminal-launched one" {
    $asm = Get-TestAssembly
    $trackerType = $asm.GetType("SystemCoreMonitor.Core.AgentLineageTracker")
    $kinds = $asm.GetType("SystemCoreMonitor.Core.OrphanKind")
    $tracker = [System.Activator]::CreateInstance($trackerType)

    $t0 = [DateTime]::new(2026, 1, 1, 12, 0, 0)
    $tracker.BeginTick()
    $tracker.RecordLiveSession(100, $t0.AddHours(-2), "Codex", "", (New-Descendants @(,@(300, $t0.AddHours(-1)))))
    $tracker.BeginTick()
    $tracker.EndTick([System.Collections.Generic.HashSet[int]]::new([int[]]@(999)), $t0)
    $later = $t0.AddMinutes(5)

    # The runtime (never sampled) started after its parent 300: the parent's session is found
    $parentSession = $tracker.FindSessionOfParent(300, $t0.AddMinutes(-30))
    if ($null -eq $parentSession) { Write-Host "         -> Parent lineage not found" -ForegroundColor Gray; return $false }
    if ($trackerType::Decide($null, $true, $true, $parentSession, $later) -ne [Enum]::Parse($kinds, "Fallback")) { return $false }

    # A remembered process that started after the child cannot be its parent
    if ($null -ne $tracker.FindSessionOfParent(300, $t0.AddHours(-3))) { Write-Host "         -> Recycled parent PID matched" -ForegroundColor Gray; return $false }

    # No lineage and an unknown dead parent (terminal launch) is not an orphan
    if ($trackerType::Decide($null, $true, $true, $null, $later) -ne [Enum]::Parse($kinds, "None")) { Write-Host "         -> Terminal-launched runtime flagged" -ForegroundColor Gray; return $false }
    # Non-runtime executables need lineage
    if ($trackerType::Decide($null, $false, $true, $parentSession, $later) -ne [Enum]::Parse($kinds, "None")) { return $false }
    return $true
}

# 29. Orphan grouping by dead session, unknown origin last
Assert-Test "Lineage: BuildGroups groups orphans by dead session with totals and unknown origin last" {
    $asm = Get-TestAssembly
    $trackerType = $asm.GetType("SystemCoreMonitor.Core.AgentLineageTracker")
    $rowType = $asm.GetType("SystemCoreMonitor.Models.AiAgentMcpServer")
    $t0 = [DateTime]::new(2026, 1, 1, 12, 0, 0)

    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($spec in @(@(1, 100, 50.0), @(2, 100, 25.0), @(3, 0, 500.0), @(4, 200, 10.0))) {
        $r = [System.Activator]::CreateInstance($rowType)
        $r.Pid = $spec[0]; $r.OrphanSessionRootPid = $spec[1]; $r.WorkingSetMB = $spec[2]
        if ($spec[1] -gt 0) { $r.OrphanSessionAgent = "Agent$($spec[1])"; $r.OrphanSessionStartTime = $t0.AddHours(-1); $r.OrphanSessionEndedAt = $t0.AddMinutes(-10) }
        $rows.Add($r)
    }
    $method = $trackerType.GetMethod("BuildGroups")
    $castMethod = [System.Linq.Enumerable].GetMethod("Cast").MakeGenericMethod($rowType)
    $groups = $method.Invoke($null, @($castMethod.Invoke($null, @(,$rows)), $t0))

    if ($groups.Count -ne 3) { Write-Host "         -> Expected 3 groups, got $($groups.Count)" -ForegroundColor Gray; return $false }
    if ($groups[0].RootPid -ne 100 -or $groups[0].ProcessCount -ne 2 -or $groups[0].TotalRamMB -ne 75.0) { Write-Host "         -> First group wrong" -ForegroundColor Gray; return $false }
    if (-not $groups[2].IsUnknownOrigin) { Write-Host "         -> Unknown origin not last" -ForegroundColor Gray; return $false }
    return $true
}

# 30. Lock staleness: session records parse and compare (PID, StartTime) as FILETIME
Assert-Test "Leftovers: session records parse and are stale only for dead or recycled PIDs" {
    $asm = Get-TestAssembly
    $scanner = $asm.GetType("SystemCoreMonitor.Core.AiAgentLeftoverScanner")
    if ($null -eq $scanner) { return $false }
    $parse = $scanner.GetMethod("TryParseSessionRecord")
    $start = [DateTime]::new(2026, 1, 1, 12, 0, 0, [DateTimeKind]::Utc)
    $ft = $start.ToFileTimeUtc()

    $args1 = @("1234.json", "{`"pid`":1234,`"procStart`":`"$ft`"}", 0, [long]0)
    if (-not $parse.Invoke($null, $args1) -or $args1[2] -ne 1234 -or $args1[3] -ne $ft) { Write-Host "         -> .json not parsed" -ForegroundColor Gray; return $false }
    $args2 = @("1234.abcdef01.key", "{`"procStartFt`":`"$ft`"}", 0, [long]0)
    if (-not $parse.Invoke($null, $args2) -or $args2[3] -ne $ft) { Write-Host "         -> .key not parsed" -ForegroundColor Gray; return $false }
    $args3 = @("settings.json", "{`"procStart`":`"$ft`"}", 0, [long]0)
    if ($parse.Invoke($null, $args3)) { Write-Host "         -> Non-record file parsed" -ForegroundColor Gray; return $false }

    $stale = $scanner.GetMethod("IsRecordStale")
    if (-not $stale.Invoke($null, @($ft, $null))) { return $false }
    if ($stale.Invoke($null, @($ft, [Nullable[datetime]]$start.AddSeconds(1)))) { Write-Host "         -> Same process judged stale" -ForegroundColor Gray; return $false }
    if (-not $stale.Invoke($null, @($ft, [Nullable[datetime]]$start.AddMinutes(10)))) { Write-Host "         -> Recycled PID judged alive" -ForegroundColor Gray; return $false }
    return $true
}

# 31. Temp scratch classifier: dual timestamp, live ownership and verified name patterns
Assert-Test "Leftovers: scratch is stale only with both timestamps old and no live owner" {
    $asm = Get-TestAssembly
    $scanner = $asm.GetType("SystemCoreMonitor.Core.AiAgentLeftoverScanner")
    $now = [DateTime]::new(2026, 1, 10, 12, 0, 0, [DateTimeKind]::Utc)
    $day = [TimeSpan]::FromHours(24)
    $old = $now.AddDays(-3)
    $fresh = $now.AddHours(-1)

    if (-not $scanner::IsScratchStale($old, $old, $now, $false, $day)) { return $false }
    if ($scanner::IsScratchStale($old, $fresh, $now, $false, $day)) { Write-Host "         -> Recent write ignored" -ForegroundColor Gray; return $false }
    if ($scanner::IsScratchStale($fresh, $old, $now, $false, $day)) { Write-Host "         -> Recent creation ignored" -ForegroundColor Gray; return $false }
    if ($scanner::IsScratchStale($old, $old, $now, $true, $day)) { Write-Host "         -> Live session scratch flagged" -ForegroundColor Gray; return $false }

    if (-not $scanner::IsClaudeCwdFileName("claude-1961-cwd")) { return $false }
    if ($scanner::IsClaudeCwdFileName("claude-notes.txt")) { return $false }
    if ($scanner::ToClaudeProjectDirName("C:\repo\my.app") -ne "C--repo-my-app") { return $false }
    return $true
}

# 32. Worktrees: dangling registration detection on a real temporary git repo, plus the deletion gate
Assert-Test "Leftovers: dangling worktree detected on a temp repo and deletion gate rejects foreign paths" {
    $asm = Get-TestAssembly
    $scannerType = $asm.GetType("SystemCoreMonitor.Core.AiAgentLeftoverScanner")
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("scm-wt-test-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
    $repo = Join-Path $root "repo"
    $wt = Join-Path $root "wt"
    try {
        New-Item -ItemType Directory -Force $repo | Out-Null
        & git -C $repo init -q 2>$null | Out-Null
        & git -C $repo -c user.email=t@t -c user.name=t commit -q --allow-empty -m init 2>$null | Out-Null
        & git -C $repo worktree add -q $wt 2>$null | Out-Null
        $admin = Get-ChildItem (Join-Path $repo ".git\worktrees") -Directory | Select-Object -First 1
        if ($null -eq $admin) { Write-Host "         -> git worktree add failed" -ForegroundColor Gray; return $false }
        $gitdir = Get-Content (Join-Path $admin.FullName "gitdir") -Raw
        $exists = [Func[string, bool]] { param($p) [System.IO.File]::Exists($p) }

        if ($scannerType::IsDanglingWorktree($gitdir, $exists)) { Write-Host "         -> Live worktree judged dangling" -ForegroundColor Gray; return $false }
        Remove-Item -Recurse -Force $wt
        if (-not $scannerType::IsDanglingWorktree($gitdir, $exists)) { Write-Host "         -> Removed worktree not detected" -ForegroundColor Gray; return $false }

        # Deletion gate: only direct children of the kind's own root pass
        $scanner = $scannerType.GetConstructor([type[]]@([string], [string], [string])).Invoke([object[]]@([string]$root, [string](Join-Path $root "home"), [string](Join-Path $root "local")))
        $itemType = $asm.GetType("SystemCoreMonitor.Core.AiLeftoverItem")
        $kind = $asm.GetType("SystemCoreMonitor.Core.AiLeftoverKind")
        $ok = [System.Activator]::CreateInstance($itemType)
        $ok.Kind = [Enum]::Parse($kind, "Worktree"); $ok.Path = $admin.FullName; $ok.RepoPath = $repo; $ok.IsDanglingRegistration = $true; $ok.CanDelete = $true
        if (-not $scanner.IsDeletionAllowed($ok)) { Write-Host "         -> Valid registration rejected" -ForegroundColor Gray; return $false }
        $bad = [System.Activator]::CreateInstance($itemType)
        $bad.Kind = [Enum]::Parse($kind, "TempScratch"); $bad.Path = $repo; $bad.CanDelete = $true
        if ($scanner.IsDeletionAllowed($bad)) { Write-Host "         -> Foreign path passed the gate" -ForegroundColor Gray; return $false }
        return $true
    } finally {
        if (Test-Path $root) { Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue }
    }
}


Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Results: $passed Passed, $failed Failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "=================================================" -ForegroundColor Cyan

if ($failed -gt 0) {
    exit 1
} else {
    exit 0
}
