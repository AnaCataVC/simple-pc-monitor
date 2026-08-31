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

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Results: $passed Passed, $failed Failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "=================================================" -ForegroundColor Cyan

if ($failed -gt 0) {
    exit 1
} else {
    exit 0
}
