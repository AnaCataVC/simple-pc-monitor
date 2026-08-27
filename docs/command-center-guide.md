# ⚡ Simple PC Monitor — Command Center & Action Buttons Technical Manual

This document provides a comprehensive technical breakdown of the interactive controls, Win32 / NT kernel P/Invoke mechanisms, concurrency invariants, and security guardrails implemented in **Simple PC Monitor v2.0.0**.

---

## 📑 Table of Contents
1. [Overview & Execution Philosophy](#1-overview--execution-philosophy)
2. [Command Center Architecture & Lifecycle](#2-command-center-architecture--lifecycle)
3. [Deep Dive: Quick Ribbon Actions](#3-deep-dive-quick-ribbon-actions)
   - [3.1 Turbo Mode (High Performance + Working Set Purge)](#31-turbo-mode-high-performance--working-set-purge)
   - [3.2 Instant DNS Resolver Flushing](#32-instant-dns-resolver-flushing)
   - [3.3 Multizone Hardened Temp Storage Cleaner](#33-multizone-hardened-temp-storage-cleaner)
   - [3.4 Hung Application Rescue Watchdog](#34-hung-application-rescue-watchdog)
4. [Deep Dive: Real-Time Process Management](#4-deep-dive-real-time-process-management)
   - [4.1 Thread Freezing (NtSuspendProcess) & Resuming (NtResumeProcess)](#41-thread-freezing-ntsuspendprocess--resuming-ntresumeprocess)
   - [4.2 Dynamic CPU Scheduler Priority Control](#42-dynamic-cpu-scheduler-priority-control)
   - [4.3 Debounced Real-Time Search & CPU % Delta Math](#43-debounced-real-time-search--cpu--delta-math)
5. [Security Invariants & Crash Prevention](#5-security-invariants--crash-prevention)
   - [5.1 Protected System Process Blacklist](#51-protected-system-process-blacklist)
   - [5.2 NTFS Reparse Point (Junction / Symlink) Isolation](#52-ntfs-reparse-point-junction--symlink-isolation)
   - [5.3 Dual Timestamp Gate & TOCTOU Defense](#53-dual-timestamp-gate--toctou-defense)

---

## 1. Overview & Execution Philosophy

Simple PC Monitor v2.0.0 is engineered with an ** Active Command Center** philosophy:
- **Zero Heavy Runtimes:** Executes as a single compiled C# WPF binary (<600 KB) with zero third-party dependencies.
- **Sub-Millisecond Direct OS Integration:** Interacts directly with native Windows dynamic-link libraries (
tdll.dll, kernel32.dll, powrprof.dll, dnsapi.dll, psapi.dll).
- **Zero-Elevation Where Possible:** Critical actions like Power Plan switching, DNS flushing, memory trimming, and process suspension operate cleanly in standard user context without triggering aggressive UAC prompts.

---

## 2. Command Center Architecture & Lifecycle

The following Mermaid diagram illustrates how user actions propagate through the UI layer, background worker threads, and native Win32/kernel APIs:

`mermaid
sequenceDiagram
    autonumber
    actor User as User
    participant UI as MainWindow (WPF)
    participant TaskRunner as Async Background Task
    participant Core as Core Subsystems
    participant OS as Windows OS / Kernel

    Note over User,OS: 1. Turbo Mode Activation
    User->>UI: Clicks Turbo Mode
    UI->>TaskRunner: Task.Run(ApplyTurboModeAsync)
    TaskRunner->>Core: PowerPlanManager.SetHighPerformance()
    Core->>OS: P/Invoke PowerSetActiveScheme(HighPerformance_GUID)
    TaskRunner->>Core: MemoryOptimizer.TrimAllWorkingSets()
    Core->>OS: P/Invoke EmptyWorkingSet() & SetProcessWorkingSetSize(-1, -1)
    TaskRunner-->>UI: Dispatches toast confirmation
    UI-->>User: Visual toast feedback (Turbo Mode Active • 184 MB Reclaimed)

    Note over User,OS: 2. Process Suspension
    User->>UI: Clicks Suspend on Task
    UI->>Core: ProcessManager.SuspendProcess(pid)
    Core->>Core: Validate against System Blacklist
    Core->>OS: P/Invoke NtSuspendProcess(processHandle)
    Core-->>UI: Updates ProcessState = Suspended
    UI-->>User: Badge shifts to Amber [Paused]
`

---

## 3. Deep Dive: Quick Ribbon Actions

### 3.1 Turbo Mode (High Performance + Working Set Purge)
- **Primary Goal:** Maximize CPU responsiveness for latency-sensitive tasks (gaming, compiling, 3D rendering) while freeing maximum physical RAM.
- **Win32 APIs:**
  - PowrProf.dll (PowerSetActiveScheme): Unparks CPU cores and switches to High Performance.
  - psapi.dll (EmptyWorkingSet) / kernel32.dll (SetProcessWorkingSetSize): Flushes unreferenced memory pages from working sets.
- **Execution Mechanism:**
  1. Activates the High Performance GUID (8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c).
  2. Concurrently enumerates non-system user processes and executes EmptyWorkingSet(handle) to release physical RAM pages.
  3. Triggers CLR Garbage Collection (GC.Collect()) on the monitoring process.

### 3.2 Instant DNS Resolver Flushing
- **Primary Goal:** Clear stale routing tables, DNS resolution errors, and cached records without opening administrative terminal sessions.
- **Win32 API:**
  - dnsapi.dll (DnsFlushResolverCache): Native call executing in <0.01 ms, clearing the Windows DNS resolver cache equivalent to ipconfig /flushdns.

### 3.3 Multizone Hardened Temp Storage Cleaner
- **Primary Goal:** Safely purge obsolete temporary files without compromising running installers, user configurations, or system integrity.
- **Target Directories:**
  1. %TEMP% (Current User temporary storage).
  2. C:\Windows\Temp (System temporary staging area).
  3. C:\Windows\WinSxS\Temp (Component servicing temporary files).
  4. C:\Windows\SoftwareDistribution\Download (Orphaned Windows Update staging payloads).
  5. C:\ProgramData\Microsoft\Windows\DeliveryOptimization (P2P cache fragments).
- **Safety Mechanisms:**
  - Files are filtered with a strict **24-hour minimum age cutoff**.
  - In-use files locked by active processes are skipped cleanly without throwing fatal exceptions.
  - Absolute exclusions guard developer caches (.claude, .antigravity), cloud sync clients (OneDrive, GoogleDrive), and modern Windows App packages (Packages).

### 3.4 Hung Application Rescue Watchdog
- **Primary Goal:** Identify and recover frozen applications that lock up the desktop.
- **Mechanism:**
  - During the telemetry refresh cycle, the process enumerator inspects process.Responding (which sends a non-blocking WM_NULL / message loop probe via Win32 SendMessageTimeout).
  - If a process with an active window handle returns Responding == false, an alert banner illuminates in the title bar.
  - Clicking *Rescue* terminates the hung process gracefully via Process.Kill(), immediately restoring desktop responsiveness.

---

## 4. Deep Dive: Real-Time Process Management

### 4.1 Thread Freezing (NtSuspendProcess) & Resuming (NtResumeProcess)
- **Native Kernel APIs (
tdll.dll):**
  - NtSuspendProcess(IntPtr processHandle): Freezes all active execution threads of the target process at the kernel scheduling level.
  - NtResumeProcess(IntPtr processHandle): Unfreezes the threads, restoring active execution.
- **Operational Flow:**
  - Unlike Process.Kill() which causes permanent data loss, NtSuspendProcess drops CPU consumption to **0.0%** instantly without closing the window or losing unsaved work.
  - When the user is ready to continue, clicking NtResumeProcess or the global *🚨 Resume All* safety button reactivates all suspended threads.

### 4.2 Dynamic CPU Scheduler Priority Control
- **Mechanism:**
  - Modifies the process base priority in the Windows scheduler via process.PriorityClass:
    - RealTime (Priority 24 — Requires elevation, reserved for critical timing).
    - High (Priority 13 — Prioritized for compilation, gaming, rendering).
    - AboveNormal (Priority 10).
    - Normal (Priority 8 — Standard Windows default).
    - BelowNormal (Priority 6 — Background renderers).
    - Idle (Priority 4 — Runs only when CPU has idle cycles).

### 4.3 Debounced Real-Time Search & CPU % Delta Math
- **Debounced UI Search:**
  - Search queries execute with a **200 ms debounce timer** to prevent UI thread lockups while filtering 250+ concurrent processes.
- **Delta-Based CPU Calculation:**
  - CPU usage for individual processes is computed using continuous sampling deltas across system processor ticks.

---

## 5. Security Invariants & Crash Prevention

### 5.1 Protected System Process Blacklist
To prevent accidental system crashes or Blue Screens of Death (BSOD), ProcessManager.cs enforces an inviolable blacklist. The following processes **CANNOT** be killed, suspended, or demoted:

| Protected Process | Subsystem Role | Consequence of Termination |
| :--- | :--- | :--- |
| csrss | Client Server Runtime Subsystem | Immediate BSOD (CRITICAL_PROCESS_DIED) |
| dwm | Desktop Window Manager | Desktop visual collapse and display reset |
| lsass | Local Security Authority | Security shutdown / reboot prompt |
| services | Service Control Manager | Fatal background services failure |
| svchost | Generic Host for Windows Services | Network and driver crash |
| xplorer | Windows Shell & Taskbar | Loss of Desktop and Start Menu |
| smss | Session Manager Subsystem | Immediate kernel halt |
| wininit | Windows Initialization Process | Critical OS initialization failure |
| winlogon | Windows Logon Process | Session termination |

### 5.2 NTFS Reparse Point (Junction / Symlink) Isolation
Traditional recursive directory cleaners traverse NTFS Directory Junctions (mklink /J) or symbolic links, inadvertently wandering into protected user folders (such as user Documents or Desktop) and deleting files older than 24 hours.

**Mitigation Implemented in SafeTempCleaner.cs:**
- Validates FileAttributes.ReparsePoint on every directory entry.
- If a subfolder inside %TEMP% is a Junction or Symlink, the cleaner **NEVER enters it recursively**, eliminating sandbox escapes.

### 5.3 Dual Timestamp Gate & TOCTOU Defense
When an installer extracts a .zip or .msi package into %TEMP%, decompression tools preserve the original modification timestamps (LastWriteTime) of files. Checking only LastWriteTime < 24h would delete active installer files midway through setup.

**Mitigation:**
The cleaner requires **BOTH** LastWriteTime < cutoff **AND** CreationTime < cutoff simultaneously. Newly unpacked files will always have a current CreationTime and are 100% protected.
