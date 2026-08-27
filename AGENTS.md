# AGENTS.md — AI Agent Guidelines & Architecture Manual

This document serves as the operational manual, architecture reference, and workflow guide for AI coding agents operating within the **Simple PC Monitor** repository.

---

## 1. Project Overview & Architecture

**Simple PC Monitor** is a high-performance, lightweight Windows desktop telemetry dashboard and power management tool built exclusively with **C# (.NET WPF/XAML)**. It provides real-time monitoring of CPU, RAM, Disk, Network Latency, Top Processes, and Windows Services in a single standalone executable (<600 KB) with zero third-party dependencies.

### Core Architecture & Modules (`src/`):
- **`Core/`**:
  - `NativeMethods.cs`: Win32 & NT kernel P/Invoke (`NtSuspendProcess`, `NtResumeProcess`, `DnsFlushResolverCache`, `GetSystemTimes`, `GlobalMemoryStatusEx`, `EmptyWorkingSet`, `SetProcessWorkingSetSize`).
  - `PowerPlanManager.cs`: Native Win32 power scheme switcher via `PowrProf.dll` (Balanced, High Performance, Power Saver).
  - `ProcessManager.cs`: Protected process manager with 16-process blacklist, Session 0 isolation, priority setter, and suspend/resume engine.
  - `ProcessMetadataCache.cs`: High-performance 0ms metadata caching (`FileDescription`, `CompanyName`, icon extraction).
  - `SafeTempCleaner.cs`: Multizone storage cleaner with anti-Junction traversal guard and dual-timestamp protection (>24h).
  - `MemoryOptimizer.cs`: Working set RAM trimmer and CLR garbage collection invoker.
  - `LocalizationManager.cs`: Real-time bilingual localization provider (ES/EN).
  - `DxgiHelper.cs` & `SetupApiHelper.cs`: DirectX DXGI GPU telemetry and SetupAPI NPU hardware discovery.
  - `SnapshotExporter.cs`: Markdown diagnostic report generator.
  - `TrayManager.cs` & `ConfigManager.cs`: System tray icon controller and persistent user settings in `%APPDATA%`.
- **`Modules/`**:
  - `CpuCollector.cs`, `MemoryCollector.cs`, `DiskCollector.cs`, `NetworkCollector.cs`, `ProcessCollector.cs` (Debounced delta % math), `ServiceCollector.cs`, `TaskCollector.cs`, `HardwareCollector.cs`, `StartupCollector.cs`, `GpuCollector.cs`, `NpuCollector.cs`.
- **`UI/` & `Views/`**:
  - `MainWindow.xaml`: Interactive Bento HUD, Ribbon action buttons, Drives storage visualizer, responsive 100% width layout.
  - `ProcessDetailsWindow.xaml`: 360° modal inspector for individual processes.
  - `App.xaml`: Dynamic 4-theme palette hot-swapper (Pastel Dark, Pastel Light, Cyberpunk, Sakura).
- **`scripts/Build-Package.ps1`**: Automated build, single-file compilation, and Setup Wizard installer packaging.

---

## 2. Directory Structure

```text
simple-pc-monitor/
├── src/
│   ├── SimplePCMonitor.csproj     # C# WPF project file (.NET Framework 4.8)
│   ├── App.xaml / App.xaml.cs     # App entrypoint and 4-theme manager
│   ├── Core/                      # Win32 P/Invoke, power plans, process guards (16 modules)
│   ├── Models/                    # Telemetry data models and hardware structs
│   ├── Modules/                   # Metric collectors (11 collectors: CPU, RAM, GPU, NPU, Disks...)
│   └── UI/                        # XAML vector gauges, custom Bento controls, dialogs
├── scripts/
│   └── Build-Package.ps1          # Dynamic MSBuild discovery and packaging pipeline
├── tests/
│   └── Metrics.Tests.ps1          # 11-Test Pester automated validation suite
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

---

## 4. Development & Build Commands (PowerShell)

### Build in Development Mode
```powershell
# Restore & build project
dotnet build src/SimplePCMonitor.csproj

# Run application
dotnet run --project src/SimplePCMonitor.csproj
```

### Run Tests
```powershell
# Run PowerShell test suite
Invoke-Pester tests/Metrics.Tests.ps1
```

### Automated Release Build & Packaging
```powershell
# Build single-file release and generate artifacts into releases/
powershell -ExecutionPolicy Bypass -File scripts/Build-Package.ps1
```

---

## 5. UI & Performance Standards

1. **Non-Blocking Telemetry**: Metric collection loops (CPU sampling, network ping, disk I/O) must execute on background threads (`Task.Run`) and dispatch UI updates asynchronously.
2. **Win32 P/Invoke Memory Safety**: Ensure all native struct marshaling (`MEMORYSTATUSEX`, `FILETIME`) allocates and frees unmanaged memory safely.
3. **No External Runtime Bloat**: Keep the executable standalone with zero external third-party DLL dependencies.
