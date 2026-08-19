# Technical Reference: Process Metadata Extraction & Zero-Latency Caching in .NET/WPF

## Overview
In Windows operating systems, raw process executable names (e.g. `mc-fw-host.exe`, `svchost.exe`, `msedgewebview2.exe`, `dwm.exe`) are cryptic identifiers. End users and system administrators require friendly product descriptions, publisher names, and deep diagnostic insights to monitor resources effectively.

This document details the architectural approach, Win32 API boundaries, high-performance concurrency caching patterns, and UI design considerations for Simple PC Monitor.

---

## 1. Win32 API & P/Invoke Boundaries

### 1.1 Process Path Retrieval (`QueryFullProcessImageName`)
Standard .NET `Process.MainModule` frequently throws `Win32Exception: Access is denied` when inspecting 64-bit processes from a 32-bit monitor or when inspecting system services without elevated UAC privileges.

To guarantee high success rates without elevation crashes:
```csharp
[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, [Out] StringBuilder lpExeName, ref int lpdwSize);

[DllImport("kernel32.dll", SetLastError = true)]
private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
```
`PROCESS_QUERY_LIMITED_INFORMATION` allows unprivileged processes to query the executable path of elevated processes (introduced in Windows Vista/Server 2008 and standard on Windows 10/11).

### 1.2 File Version Info Extraction
Once the executable path is obtained, `System.Diagnostics.FileVersionInfo.GetVersionInfo(path)` reads the `VS_VERSIONINFO` resource block containing:
- `FileDescription` (e.g., `"Microsoft Edge WebView2 Runtime"`, `"Google Chrome"`)
- `CompanyName` (e.g., `"Microsoft Corporation"`, `"Google LLC"`)
- `FileVersion` & `ProductVersion`
- `LegalCopyright`

---

## 2. Zero-Latency Concurrency Caching (`ProcessMetadataCache`)

### 2.1 The Performance Problem
Querying disk I/O and reading PE version tables on every 1-5 second polling interval for 10-20 processes adds ~5-15 ms of overhead and disk reads per cycle.

### 2.2 Solution: Thread-Safe In-Memory Cache
Because a process's on-disk metadata (`FileDescription`, `CompanyName`) is immutable throughout its execution lifetime, we cache it in a thread-safe `ConcurrentDictionary<string, ProcessMetadataInfo>`.

1. **Pre-baked OS Kernel Mapping**: Static fallback dictionary for core OS processes (`System`, `Idle`, `smss`, `csrss`, `wininit`, `services`, `lsass`, `svchost`, `dwm`, `explorer`, `sihost`, `taskhostw`, `RuntimeBroker`, `SearchHost`, `ctfmon`).
2. **Path-Based Cache**: Lookups keyed by executable path or process name execute in **< 0.0001 ms** (O(1)).
3. **Graceful Fallback**: If file access fails or description is empty, the cache stores the clean process name, ensuring disk is not repeatedly queried for inaccessible virtual processes.

---

## 3. UI/UX Architecture & Theme Integration

### 3.1 Process Row Presentation (Dual-Line View)
- **Primary Line**: Friendly Name (Bold, Accent Text). If unavailable, defaults cleanly to Process Name.
- **Secondary Line**: Executable Name & Publisher (`chrome.exe • Google LLC`), subtle muted color.
- **Inline Actions**: Quick Kill (`✕ End`), File Explorer (`📂 Open`), and Detailed Inspection (`ℹ️ Info`).

### 3.2 Inspector Window (`ProcessDetailsWindow`)
- **Modal Dialog**: Custom dark/neon/light/rose styled WPF window matching application theme tokens.
- **Metrics Breakdown**: Working Set, Peak Working Set, Private Bytes, Paged Bytes, Virtual Memory, Thread & Handle counts, Start Time, Uptime, Architecture (x64/x86), and Priority.
- **Interactive Actions**: Copy diagnostic dump, Open folder, Search online, End task.

---

## 4. Verification & Compatibility Matrix
- **Target Framework**: .NET Framework 4.5+ (Standard on all Windows 10 and 11 installations).
- **Dependencies**: 0 NuGet dependencies (Native WPF, PresentationFramework, Kernel32 P/Invoke).
- **DPI Scaling**: Per-monitor DPI aware via WPF vector graphics (`VectorIcons.xaml`).
