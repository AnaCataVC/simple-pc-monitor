# AGENTS.md — AI Agent Guidelines & Architecture Manual

This document serves as the operational manual, architecture reference, and workflow guide for AI coding agents operating within the **System Core Monitor** repository.

---

## 1. Project Overview & Architecture

**System Core Monitor** is a high-performance, lightweight Windows desktop telemetry dashboard and power management tool built exclusively with **C# (.NET 9 WPF/XAML)**. It provides real-time monitoring of CPU, RAM, Disk, Network Latency, Top Processes, and Windows Services in a single standalone executable (<650 KB) with zero third-party dependencies.

### Core Architecture & Modules (`src/`):
- **`Core/`**:
  - `NativeMethods.cs`: Win32 & NT kernel P/Invoke (`NtSuspendProcess`, `NtResumeProcess`, `NtQuerySystemInformation` (per-core CPU times), `DnsFlushResolverCache`, `GetSystemTimes`, `GlobalMemoryStatusEx`, `EmptyWorkingSet`, `SetProcessWorkingSetSize`, `WM_GETMINMAXINFO`, `MonitorFromWindow`, `GetMonitorInfo`, `CreateToolhelp32Snapshot`, `AttachConsole`, `GenerateConsoleCtrlEvent`).
  - `CrashLogger.cs`: Enterprise resilient crash logging with 1MB size cap, `.old` log rotation, sliding rate limiting (5 logs/10s), and global exception traps (`AppDomain`, `TaskScheduler`, `Dispatcher`).
  - `PowerPlanManager.cs`: Native Win32 power scheme switcher via `PowrProf.dll` (Balanced, High Performance, Power Saver).
  - `ProcessManager.cs`: Two-phase graceful close engine (`RequestGracefulCloseAsync`), reverse topological tree termination (`TerminateProcessTree`), 16-process protected blacklist, Session 0 isolation, priority setter, and suspend/resume engine. `TerminateProcessTree` enforces the PID-reuse invariant on both ends: `CollectTreeNodes` refuses to descend into a "child" that started before its parent (Windows never clears a recorded parent PID, so a live process pointing at a recycled number is a stranger), and the optional `expectedStartTime` argument aborts the whole termination when the root PID no longer identifies the process the caller sampled.
  - `ProcessMetadataCache.cs`: High-performance 0ms metadata caching (`FileDescription`, `CompanyName`, icon extraction).
  - `SafeTempCleaner.cs`: Multizone storage cleaner with anti-Junction traversal guard and dual-timestamp protection (>24h). Also exposes `CleanWhitelistedCache`, which clears a single regenerable cache root with the exclusion list disabled (those substring exclusions are tuned for live app data inside `%TEMP%` and would wrongly spare legitimate cache roots such as NuGet's `packages`); `IsSafeTempRoot` still blocks drive roots and system/profile directories, and callers must whitelist the path first.
  - `FileSystemSafety.cs`: Shared storage guards used by the cleaner, the scanner, the bloat detector and `DiskCollector`. `IsReparsePoint` fails closed (unreadable attributes are treated as a reparse point, so traversal refuses to descend); `IsLikelyVirtualVolume` classifies cloud mounts that Windows reports as fixed drives, taking primitives rather than a `DriveInfo` so it stays unit-testable.
  - `FolderSizeScanner.cs`: Read-only recursive folder-size breakdown with `IProgress<T>` reporting, `CancellationToken` support, parallel first-level subtree walking and reparse-point exclusion. `MeasureTotalBytes` is the shared single-directory total, so no second traversal implementation exists in the codebase.
  - `BloatDetector.cs`: Detects Docker/WSL VHDX growth, regenerable build caches, the Recycle Bin (`SHQueryRecycleBin`), paging/hibernation files and the WinSxS component store. Owns the deletion whitelist: `IsWhitelistedForDeletion` is an exact normalized match and is the only gate through which `DeleteWhitelistedCache` will remove anything.
  - `MemoryOptimizer.cs`: Working set RAM trimmer and CLR garbage collection invoker.
  - `LocalizationManager.cs`: Real-time bilingual localization provider (ES/EN).
  - `DxgiHelper.cs` & `SetupApiHelper.cs`: DirectX DXGI GPU telemetry and SetupAPI NPU hardware discovery.
  - `SnapshotExporter.cs`: Markdown diagnostic report generator.
  - `TrayManager.cs` & `ConfigManager.cs`: System tray icon controller and persistent user settings in `%APPDATA%`.
  - `ToolLauncher.cs`: Shared launchers for external Windows tools (Task Manager, Resource Monitor, Reliability Monitor, PC Manager/Storage Sense, Services console, Task Scheduler), reused across `MainWindow.xaml.cs` handlers instead of each reimplementing `Process.Start`.
  - `CpuUsageTracker.cs`: Shared PID-keyed CPU delta tracker (dictionary of previous `TotalProcessorTime`/timestamp samples), used by both `ProcessCollector` and `AiAgentCollector` instead of each maintaining its own copy.
  - `TimedCache.cs`: Generic `TimedCache<T>` wrapping the "cache for N seconds unless force-refreshed" pattern shared by `ServiceCollector`, `StartupCollector`, `DiskCollector`, and `TaskCollector`.
  - `AgentLineageTracker.cs`: Remembers every process seen inside a live agent session, keyed by `(PID, StartTime)`. When the session's root dies, any remembered descendant still alive is an orphan whatever its executable (`OrphanKind.Lineage`, after a 10 s grace). Without lineage, only a known runtime whose dead parent belonged to an ended session qualifies (`OrphanKind.Fallback`), so a runtime launched from a closed terminal is not reported. Dead descendants stay remembered for `DeadLineageRetention` as parent evidence; `EndTick` is a no-op on an empty snapshot. `BuildGroups` groups orphans per dead session with an "unknown origin" bucket last. Kills from the AI Agents tab go through a Yes/No confirmation listing names, reasons, ages and total RAM, then `TerminateProcessTree` with each row's sampled `StartTime`.
  - `AiAgentLeftoverScanner.cs`: On-demand, read-only scan (IProgress + CancellationToken) of disk leftovers from agent sessions: dangling `.git/worktrees/*` registrations and abandoned `<repo>/.claude/worktrees/*` (repos discovered from the cwd in each Claude project's newest transcript; dirty worktrees are report-only), stale per-project scratch under `%TEMP%\claude` and `claude-*-cwd` markers (dual 24 h timestamp, never a live session's folder), `~/.claude/sessions` records whose `(PID, StartTime)` no longer matches, and MCP logs under `%LOCALAPPDATA%\claude-cli-nodejs\Cache\*\mcp-logs-*` older than the transcript retention. `IsDeletionAllowed` is an exact-match gate against each kind's root; worktrees are removed only through `git worktree prune`/`remove`.
  - `RunawayProcessDetector.cs`: Pure runaway heuristics. `ClassifyScan` flags find/rg/fd/findstr/where/robocopy, `cmd` with `dir /s` and PowerShell with `-Recurse` only when an argument is a drive root or the whole user profile (scoped paths never match); `SustainedLoadTracker` flags a process held above `CpuThresholdPercent` for `MinSustained`, keyed on `(PID, StartTime)` and evicting only on a non-empty snapshot. `ProcessCollector` exposes the result as `LastRunawayProcesses`; the Processes view lists them and ends one only after a confirmation, through `TerminateProcessTree` with the sampled `StartTime`.
  - `MetricFormatting.cs`: Shared byte→GB conversion, auto-scaled human-readable byte formatting, and `Crit`/`Warn`/`Ok` percentage threshold classification, and the shared `FormatAge` duration label, used across the collectors.
- **`Models/`**:
  - `SystemMetrics.cs`: Strongly typed telemetry DTOs, hardware metrics, and process data structures.
  - `AiAgentSession.cs`: AI developer session models (`ParentPid`, `AgentName`, `StartTime`, `SessionContext`, `ModelName`), child MCP server subprocess models (`AiAgentMcpServer`, `RoleBadgeColor`, `IsMcpServer`), decoupled metrics (`ChildProcessCount` vs `McpServersCount`), dynamic status visual binding (`StatusBadgeColor`, `StatusDisplay`), conditional model badge (`🧬 <ModelName>`), resumed CLI hash naming (`🔗 Sesión <8-char-hash>`), consolidated RAM/CPU metrics (`TotalWorkingSetMB`, `TotalCpuPercent`), and the orphan report (`AiAgentMetric.OrphanProcesses` plus `ParentPid`, `OrphanReason`, `AgeDisplay` on `AiAgentMcpServer`, which doubles as the orphan row, tagged with its dead session's identity), plus `AiAgentOrphanGroup` (one expandable row per ended session).
- **`Modules/`**:
  - `CpuCollector.cs` (aggregate % via `GetSystemTimes` plus per-core loads via `NtQuerySystemInformation`; a core counts as in use at ≥ `ActiveCoreThresholdPercent`, covering one processor group of up to 64 logical processors), `MemoryCollector.cs`, `DiskCollector.cs`, `NetworkCollector.cs`, `ProcessCollector.cs` (Thread-safe debounced delta % math with `_syncLock` and fast in-memory sorting), `AiAgentCollector.cs` (Atomic Win32 Toolhelp32 process tree scanner & MCP session aggregator hardened with `_sampleGate` anti-reentrancy lock, deterministic `SafeProcessHandle` disposal via `using`/`Dispose()`, cold-start PEB protection without premature negative caching, snapshot cache-eviction safeguard (`allRunningPids.Count > 0`), dynamic purge of `CollapsedSessionPids` against kernel PID reuse, launcher isolation `npx`/`uvx`, Go/Rust compiled MCP discovery via CLI markers, independent session boundary pruning, and orphan detection via `CollectOrphans`, which feeds each live session's descendants to `AgentLineageTracker` and reports what an ended session left behind, grouped per dead session in `AiAgentMetric.OrphanGroups`), `ServiceCollector.cs`, `TaskCollector.cs`, `HardwareCollector.cs`, `StartupCollector.cs`, `GpuCollector.cs`, `NpuCollector.cs`.
- **`UI/` & `Views/`**:
  - `MainWindow.xaml` & `MainWindow.xaml.cs`: Interactive Bento HUD, Ribbon action buttons, AI Agents Tab with decoupled MCP / child process counter badges and dynamic `StatusBadgeColor` visual state binding (`#10B981` Emerald vs `#64748B` Slate), Drives storage visualizer, `WM_GETMINMAXINFO` multi-monitor DPI hook, and `ApplyProcessSortingFast`.
  - `ProcessDetailsWindow.xaml`: 360° modal inspector with two-phase graceful close for individual processes.
  - `App.xaml` & `App.xaml.cs`: Application entrypoint, `CrashLogger` initialization, and dynamic 4-theme palette switcher (Pastel Dark, Pastel Light, Cyberpunk, Sakura).
  - `Themes/CommonStyles.xaml`: Vector-based `ActivePillActionButtonStyle` and unified control templates.
- **`scripts/Build-Package.ps1`**: Automated build, single-file compilation, and Setup Wizard installer packaging.

---

## 2. Directory Structure

```text
system-core-monitor/
├── src/
│   ├── SystemCoreMonitor.csproj   # C# WPF project file (.NET 9 SDK-style)
│   ├── App.xaml / App.xaml.cs     # App entrypoint, CrashLogger traps, and 4-theme manager
│   ├── app.manifest               # Per-Monitor DPI V2 & Windows 10/11 compatibility
│   ├── Core/                      # Win32 P/Invoke, crash logging, power plans, process & storage guards (29 modules)
│   ├── Models/                    # Telemetry data models and AI Agent / MCP structures
│   ├── Modules/                   # Metric collectors (12 collectors: CPU, RAM, AI Agents, GPU, NPU, Disks...)
│   ├── ViewModels/                # MVVM presentation layer (ViewModelBase, Main, Dashboard, AiAgents...)
│   ├── Views/                     # Modular XAML views (DashboardView, AiAgentsView, StorageView...)
│   └── UI/                        # Windows, legacy dialogs, vector icons, themes, value converters
├── scripts/
│   └── Build-Package.ps1          # Single-file .NET 9 publish and packaging pipeline
├── tests/
│   ├── Metrics.Tests.ps1          # 32-Test Health & Reflection validation suite
│   ├── AiTranscript.Tests.ps1     # 5-Test AI Transcript Retention & Cleanup suite
│   └── DeepStress.Tests.ps1       # 6-Test Live Process Tree, PID Reuse Guard, Handle Leak & 5s Smoke suite
├── releases/                      # Standalone executables, ZIPs, installers (gitignored)
├── docs/                          # Architecture guides, command center manual, benchmarks
└── README.md                      # Bilingual project documentation (EN/ES)
```

---

## 3. Mandatory Agent Rules & Directives

### 🌐 Language & Communication
- **Source Code**: All C# code (classes, methods, properties), XAML attributes, and comments MUST be in **English**.
- **User Chat**: Communicate with the user in **Spanish** unless requested otherwise.
- **Git Commits**: Use **Conventional Commits** in **English** (e.g., `feat: ...`, `fix: ...`, `docs: ...`, `refactor: ...`).
- **README**: Maintain bilingual documentation (English and Spanish).

### 🔒 Security & Privacy
- **Absolute Paths**: NEVER leak absolute user paths (e.g., `C:\Users\...`) into code, documentation, or commit logs. Use relative paths or environment placeholders (`%LOCALAPPDATA%`, `%TEMP%`).
- **Process Guardrails**: NEVER remove or bypass the protected system process blacklist in `ProcessManager.cs`.

### 💻 PowerShell Environment
- **Command Chaining**: NEVER use `&&` or `||` in terminal commands. Use `;` or separate sequential commands.
- **GitHub CLI Context**: Switch to personal account `AnaCataVC` (`gh auth switch -u AnaCataVC --hostname github.com 2>$null`).

### 📦 Release Distribution Invariant (Single Deliverable Policy)
- **Single Official Deliverable**: Every GitHub Release for System Core Monitor must publish **ONLY** the Setup Wizard installer:
  ```text
  SystemCoreMonitor-Setup.exe
  ```
- **Strict Prohibition on Extra Assets**: NEVER attach `SystemCoreMonitor.exe` (standalone binary) or `System-Core-Monitor-*-Portable.zip` to public GitHub Releases. The standalone executable is compiled strictly as an embedded payload for `SystemCoreMonitor-Setup.exe` and for local test suite execution via reflection (`tests/Metrics.Tests.ps1`), never as an independent release download.
- **Verification Invariant**: All release workflows and subagents (`ami-release-manager`) must verify via `gh release view <tag> --json assets` that exactly 1 asset exists: `SystemCoreMonitor-Setup.exe`.

---

## 4. Development & Build Commands (PowerShell)

### Build in Development Mode
```powershell
# Restore & build project with .NET 9 CLI
dotnet build src\SystemCoreMonitor.csproj -c Release
```

### Run Tests (43 Automated Tests)
```powershell
# 1. Run Health & Architecture Tests (32 tests)
pwsh -ExecutionPolicy Bypass -File tests\Metrics.Tests.ps1

# 2. Run AI Transcript Retention & Cleanup Tests (5 tests)
pwsh -ExecutionPolicy Bypass -File tests\AiTranscript.Tests.ps1

# 3. Run Deep Stress, PID Reuse Guard, Handle Leaks & Smoke Tests (6 tests)
pwsh -ExecutionPolicy Bypass -File tests\DeepStress.Tests.ps1
```

> All suites load `releases/SystemCoreMonitor.exe` by reflection under .NET 9 runtime, and that folder is gitignored. On a
> fresh clone run `scripts/Build-Package.ps1` first, or the health tests fail on a missing
> file rather than on anything you changed.

### Automated Release Build & Packaging
```powershell
# Build single-file release and generate artifacts into releases/
powershell -ExecutionPolicy Bypass -File scripts/Build-Package.ps1
```

---

## 5. UI & Performance Standards

1. **Non-Blocking Telemetry**: Metric collection loops (CPU sampling, network ping, disk I/O) must execute on background threads (`Task.Run`) and dispatch UI updates asynchronously.
2. **Win32 P/Invoke Memory Safety**: Ensure all native struct marshaling (`MEMORYSTATUSEX`, `FILETIME`, `PROCESSENTRY32`) allocates and frees unmanaged memory safely in `try/finally` blocks.
3. **No External Runtime Bloat**: Keep the executable standalone with zero external third-party DLL dependencies.
4. **Deterministic Win32 Handle Cleanup**: Every `Process.GetProcessById()` or unmanaged Win32 handle instantiation must be wrapped in `using (...)` or disposed in `finally` to prevent `SafeProcessHandle` leaks in native kernel memory under continuous polling.
5. **Metric Collector Anti-Reentrancy**: Stateful delta collectors (such as `AiAgentCollector` and `ProcessCollector`) must guard their time-series calculations, delegated to the shared `CpuUsageTracker`, with re-entrancy locks (`_sampleGate`) to prevent race conditions during concurrent sampling passes.
6. **Process Cold-Start Resilience**: When inspecting process command lines via `NtQueryInformationProcess`, handle the uninitialized PEB state (`cmd == null`) gracefully without poisoning negative caches, allowing future cycles to classify the process once fully initialized.
7. **Toolhelp32 Snapshot Cache Eviction Safeguard**: Cache evictions (the `CpuUsageTracker` samples, `_sessionContextCache`, `_childProcessCache`, and `CollapsedSessionPids`) must be strictly guarded behind `if (allRunningPids.Count > 0)`. Transient snapshot failures during heavy OS resource contention must never wipe historical CPU baselines or reset user UI collapse states.
8. **WPF Null/Empty DataTrigger Resilience**: Optional string metadata visualized inside styled containers (such as `ModelName` in the session header) must declare dual `DataTrigger` rules for both `Value=""` and `Value="{x:Null}"` to collapse the container cleanly without leaving empty background boxes or visual gaps.
9. **Collector Enumeration Caching**: Collectors whose underlying OS enumeration is expensive and slow-changing (installed services, startup entries, drive info) must cache the result for a short `TimeSpan` (with an optional `forceRefresh` parameter) instead of re-enumerating on every poll tick, mirroring the pattern in `TaskCollector.cs`, `ServiceCollector.cs`, `StartupCollector.cs`, and `DiskCollector.cs`.
10. **Long Operations Report Progress and Accept Cancellation**: Work that can run for minutes (filesystem scans, bulk analysis) must never be a blocking spinner. It takes an `IProgress<T>` and a `CancellationToken`, checks the token at coarse boundaries (per directory, not per file), and **throttles progress reports by elapsed time** (~150 ms) rather than by item count — an unthrottled report per item floods the WPF `Dispatcher` on trees full of tiny entries. Cancellation is checked cooperatively and returns normally instead of throwing across parallel workers, so `Parallel.*` never wraps the result in an `AggregateException`. The reference implementation is `FolderSizeScanner.cs`.
11. **On-Demand Work Never Joins the Telemetry Tick**: Expensive user-initiated analysis must be triggered only from an explicit user action and dispatched with `Task.Run`, never wired into the periodic collector tick that refreshes the HUD. Results are held in a field for redraw; `TimedCache<T>` is for tick-driven metrics where a stale-by-seconds value is still valid, not for scans the user expects to re-run deliberately.
12. **A PID Is Not an Identity, and the Kill Path Must Check It Too**: Windows reuses process IDs and never clears a recorded parent PID, so a PID sampled one tick ago can name a different process by the time the user clicks, and a live process can point at a recycled parent number while belonging to someone else entirely. The pair `(PID, StartTime)` is the identity. Any code that **terminates** must apply the same invariant the reading side already does: a tree walk skips a "child" that started before its parent, and a termination driven by a sampled list passes that sample's `StartTime` so the operation aborts instead of killing a stranger. Reading code that is merely wrong shows a bad number; terminating code that is wrong destroys someone's work, so the check belongs on the destructive path first.
13. **Detection Reports, the User Decides**: A heuristic that classifies a process as unwanted (orphan detection being the current case) presents its evidence — the reason, the age, the sanitized command line — and never acts on its own. A dead parent is normal for plenty of legitimate processes launched from a terminal that has since closed, so the classifier is a filter for human attention, not a verdict. Bulk actions over such a list stay behind an explicit confirmation that states what was matched and why.

14. **Filesystem Traversal Is Reparse-Point Aware and Whitelist-Gated**: Any new code that walks directories must refuse to descend into reparse points via `FileSystemSafety.IsReparsePoint` (they project data that does not occupy this volume), must use manual recursion over `TopDirectoryOnly` rather than `SearchOption.AllDirectories`, and must never delete a path that has not passed an exact-match whitelist check. Deletion paths are always derived from a known constant, never from user input or from a scan result.
15. **WPF Binding Fallback Value Invariant**: `FallbackValue` in WPF XAML data bindings strictly accepts literal values (e.g., `FallbackValue='N/A'`). It structurally fails at runtime with `XamlParseException` if passed a nested `{Binding ...}` expression. Dynamic fallbacks must be calculated directly in the model or viewmodel (such as `ProcessMetric.DisplayTitle`).
16. **Non-Blocking Window Responsiveness (`IsHungAppWindow`)**: Never call .NET's `Process.Responding` across hundreds of processes in a periodic sampling loop. `Process.Responding` issues a synchronous `SendMessageTimeout` with a 5,000 ms ceiling per hung window. Use Win32 User32 `IsHungAppWindow(hWnd)` which inspects internal OS message queues without issuing synchronous messages and returns in 0 ms.
17. **Two-Phase Process Telemetry Sampling**: Process sampling loops must split work into two phases: Phase 1 captures PID, Name, WorkingSet, and CPU delta across all processes in memory (<15 ms) and sorts to extract the top candidates (e.g., top 100-150); Phase 2 enriches only the top candidates with window titles, priority, and metadata, immediately disposing the remaining hundreds of unselected processes.
18. **Decoupled Telemetry Dispatching**: Do not bundle ultra-fast telemetry loops (CPU, RAM, Disks, Top Processes ~15-40 ms) with slower background analyzers (AI session tree scanning, SetupAPI hardware discovery ~1-2 s) in a monolithic `Task.WhenAll`. Await fast metrics first and update interactive ViewModels immediately (<40 ms), allowing slower background telemetry to update its tabs asynchronously as it resolves.
