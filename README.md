# Simple PC Monitor 🖥️⚡

<div align="center">

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=flat-square&logo=windows)](https://www.microsoft.com/windows)
[![C# .NET](https://img.shields.io/badge/C%23-WPF%20%2F%20XAML-512BD4?style=flat-square&logo=csharp)](https://dotnet.microsoft.com/)
[![Version](https://img.shields.io/badge/Release-v2.4.0-93A8FD?style=flat-square)](https://github.com/AnaCataVC/simple-pc-monitor/releases/tag/v2.4.0)
[![Binary Size](https://img.shields.io/badge/Binary%20Size-585%20KB-success?style=flat-square)]()
[![Tests](https://img.shields.io/badge/Tests-20%20Passed-brightgreen?style=flat-square)]()
[![Antivirus](https://img.shields.io/badge/Antivirus-0%20False%20Positives-7EE7B8?style=flat-square)]()
[![License](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)

*A high-performance, lightweight, and interactive Windows desktop command center engineered in compiled Native C# (.NET WPF/XAML). Features zero external dependencies, sub-millisecond Win32 P/Invoke telemetry, AI Agent & MCP Session Monitor, Two-Phase Graceful Process Termination, kernel-level process control (NtSuspend/NtResume), 1-click power plans, multizone hardened storage cleaning, interactive Bento metric cards, responsive multi-drive analytics, enterprise crash logging, seamless multi-monitor DPI maximization, and zero-heuristic footprint in a standalone 585 KB binary.*

[English](#-english) • [Español](#-español)

</div>

---

## 🇺🇸 English

### 1. Project Description
**Simple PC Monitor v2.4.0** is an interactive desktop command center and telemetry suite built exclusively with compiled C# and Windows Presentation Foundation (WPF). It monitors and actively manages critical system resources—**CPU, Memory (RAM), Multi-Drive Storage, Network Latency & Throughput, Real-Time Processes, AI Agents & Model Context Protocol (MCP) Sessions, Windows Services, Scheduled Tasks, Startup Applications, and Hardware Accelerators (GPU/NPU)**—packaged into a single standalone `.exe` without third-party runtimes or background services.

---

### 2. ⚡ Command Center & Action Buttons Reference

Simple PC Monitor transitions from a passive observer to an **Active Command Center**. Below is the exact behavior and Win32/Kernel API mechanism behind every interactive control in the HUD:

| Action / Button | UI Location | Mechanism & Native Win32 / Kernel API | Exact Behavior & Purpose |
| :--- | :--- | :--- | :--- |
| **🚀 Turbo Mode** | Top Ribbon / Tray | Win32 `PowrProf.dll` (`PowerSetActiveScheme`) + `EmptyWorkingSet` | Instantly switches the Windows power plan to **High Performance** (unparking CPU cores) and concurrently purges idle working set memory pages across user processes to reclaim physical RAM. |
| **🤖 AI Agent & MCP Monitor** | AI Agents Tab | Win32 `CreateToolhelp32Snapshot` + `SafeProcessHandle` + `_sampleGate` + PID Reuse Gate + Snapshot Resilience | Discovers AI developer CLIs (`claude.exe`, `gemini.exe`, `codex.exe`, `aider.exe`, `ollama.exe`, `cursor.exe`, `antigravity.exe`), identifies resumed CLI sessions via hash/UUID (`--resume=`, `🔗 Sesión <8-char-hash>`), displays dynamic AI Model badges (`--model`, `🧬 <ModelName>`), decouples total child processes (`ChildProcessCount`) from verified MCP servers (`McpServersCount`), isolates ephemeral launchers (`npx`, `uvx`), detects Go/Rust compiled MCP binaries via CLI markers, promotes independent CLI sessions with tree boundary truncation, guards snapshot cache-eviction (`allRunningPids.Count > 0`), and dynamically reflects Active (Emerald `#10B981`) vs. Idle (Slate `#64748B`) states. |
| **🛑 Two-Phase Graceful Close** | Process & AI Tabs | `CloseMainWindow` / `WM_CLOSE` + Tray Detection | **Phase 1**: Dispatches a non-blocking graceful close request and detects if the app minimized to the System Tray (`MainWindowHandle == IntPtr.Zero`). **Phase 2**: Prompts for force termination only if the process remains active or unresponsive. |
| **⚡ Reverse Tree Kill** | AI Agents Tab | Reverse Topological Tree Termination | Terminates entire process trees in reverse topological order (leaf MCP subprocesses first $\rightarrow$ root CLI last) eliminating orphaned background processes and memory leaks. |
| **🌐 Flush DNS** | Top Ribbon | Native `dnsapi.dll` (`DnsFlushResolverCache`) | Directly purges and resets the Windows DNS name resolver cache in 0.01 ms, resolving stale routes, domain lookup glitches, and network timeouts without needing CMD. |
| **🧹 Clean Temp** | Top Ribbon | Multizone `SafeTempCleaner` (>24h Cutoff) | Safely cleans obsolete cache files in `%TEMP%`, `C:\Windows\Temp`, `WinSxS\Temp`, `SoftwareDistribution\Download`, and `DeliveryOptimization`. Protected by **NTFS Reparse Point (Junction/Symlink) isolation** and **Dual Timestamp Gate** (`CreationTime` + `LastWriteTime`). |
| **⚠️ Rescue Process** | Dynamic Title Alert | Win32 `IsResponding` Watchdog + Two-Phase Close | Real-time watchdog detects windowed processes that stop responding to the Windows message loop (`IsResponding == false`). Clicking *"Rescue"* dispatches a safe two-phase close. |
| **⏸️ Suspend Process** | Process List / Context | Kernel `ntdll.dll` (`NtSuspendProcess`) | Freezes all execution threads of a CPU-intensive or runaway background task, dropping its CPU consumption to 0.0% instantly without closing the window or losing unsaved work. |
| **▶️ Resume Process** | Process List / Context | Kernel `ntdll.dll` (`NtResumeProcess`) | Safely reactivates a suspended process, restoring its threads to active scheduling immediately. |
| **🚨 Resume All** | Process Tab Toolbar | Batch `NtResumeProcess` Watchdog | Global emergency safety button that immediately unfreezes all currently suspended user processes. |
| **⚡ CPU Priority Selector** | Context Menu | Win32 `ProcessPriorityClass` (`SetPriorityClass`) | Modifies the Windows CPU scheduler priority in real time (`Realtime`, `High`, `AboveNormal`, `Normal`, `BelowNormal`, `Idle`) to prioritize gaming, compiling, or rendering. |
| **🔍 Real-Time Search** | Process Tab Toolbar | 200 ms Debounced Filter | Reactively filters active tasks by executable name, PID, or friendly business metadata without UI thread jitter. |
| **⚡ Fast In-Memory Sorting** | Process Tab Toolbar | `ApplyProcessSortingFast` + `_syncLock` | Instantly toggles between CPU % and RAM (MB) descending sorts in-memory without blocking the UI thread or triggering redundant OS process enumeration. |
| **🎛️ Interactive Bento Cards** | Main HUD Dashboard | WPF Event Routing & Filter Dispatcher | Clicking any Bento tile (CPU, RAM, GPU, Disk, Network) redirects directly to the detailed view and applies the relevant sorting filter. |
| **⚡ Trim RAM** | Memory Card / Tray | Win32 `SetProcessWorkingSetSize` / `EmptyWorkingSet` | Trims unreferenced memory pages from the process working set and executes CLR Garbage Collection. |
| **📌 Always on Top (Pin)** | Window Titlebar | WPF `Topmost` Property Toggle | Pins the telemetry window above fullscreen apps, games, or IDEs for uninterrupted monitoring. |
| **🗖 Seamless Maximization** | Window Controls | Native Win32 `WM_GETMINMAXINFO` Hook | Intercepts `0x0024` message and calculates work area via `MonitorFromWindow`, eliminating taskbar clipping and multi-monitor bleed on borderless custom chrome. |
| **🎨 4 Modern Themes** | Titlebar Palette | `ActivePillActionButtonStyle` & Dynamic Resources | Instantly swaps between **Pastel Dark**, **Pastel Light**, **Cyberpunk Neon**, and **Sakura Rose** with dynamically themed vector buttons. |

> [!TIP]
> 📖 **Deep Technical Architecture & Security Manual:** For full Mermaid execution sequence diagrams, Win32 P/Invoke invariants, and TOCTOU/Junction safety mechanics, see the dedicated **[Command Center Technical Manual](docs/command-center-guide.md)**.

---

### 3. Key Highlights & Capabilities:
- **🤖 AI Agent & MCP Session Monitor:** Real-time discovery and consolidated telemetry of developer AI sessions, resumed session identification via CLI hash/UUID (`--resume=`, `🔗 Sesión <8-char-hash>`), dynamic AI model badges (`--model`, `🧬 <ModelName>`) with null/empty-safe WPF DataTriggers, decoupled MCP vs. child counters, Toolhelp32 snapshot failure resilience (`allRunningPids.Count > 0`), cold-start PEB resilience, and dynamic Active/Idle state badges.
- **⚡ Zero-Leak Handle Architecture & Concurrency Guard:** Deterministic disposal of Win32 process handles (`SafeProcessHandle` via `using`/`Dispose()`) and anti-reentrancy synchronization (`_sampleGate`) for rock-solid CPU delta calculations under high-frequency polling.
- **🛡️ Two-Phase Graceful Close Protocol:** Safe non-blocking process termination with System Tray minimization detection and topological tree kill.
- **Responsive 100% Full-Width Bento Grid:** Eliminates dead UI margins, delivering clean, high-density telemetry across all monitor aspect ratios.
- **Enterprise Crash Logging & Exception Traps:** Centralized `CrashLogger` captures unhandled domain exceptions, unobserved task faults, and recoverable UI dispatcher errors with a 1MB size cap, sliding rate limiting, and log rotation.
- **Dedicated Multi-Drive Storage Hub:** Live partition visualizer with filesystem health, drive type detection (NVMe/SSD/HDD), activity meters, and 1-click Explorer shortcuts.
- **🔍 360° Process Inspector & Metadata Resolver:** Resolves human-readable application names (`FileDescription`), verified publishers (`CompanyName`), architectures, and window titles with 0ms cache overhead.
- **Protected System Process Blacklist:** Hardened guardrails strictly prevent accidental termination or suspension of essential operating system services (`csrss`, `dwm`, `svchost`, `explorer`, `services`, `lsass`).
- **ICMP Ping Latency Monitor:** Constant background network latency measurement without UI locking.
- **Diagnostic Snapshot Exporter:** Generates full Markdown health reports with one click for sharing or debugging.

---

### 4. Architecture & Modular Structure

```text
simple-pc-monitor/
├── src/
│   ├── SimplePCMonitor.csproj      # C# WPF project file (.NET Framework 4.8)
│   ├── App.xaml & App.xaml.cs      # Entrypoint, CrashLogger bootstrap & 4-theme switcher
│   ├── Core/
│   │   ├── NativeMethods.cs        # Win32 & NT kernel P/Invoke (ntdll, user32, dnsapi, powrprof, toolhelp32)
│   │   ├── CrashLogger.cs          # Resilient crash logging (1MB cap, rotation, rate limiting)
│   │   ├── PowerPlanManager.cs     # Native Win32 power scheme switcher
│   │   ├── ProcessManager.cs       # Two-phase graceful close, reverse topological tree kill & blacklist guards
│   │   ├── ProcessMetadataCache.cs # High-performance 0ms metadata caching
│   │   ├── SafeTempCleaner.cs      # Hardened multizone storage cleaner (Anti-TOCTOU & Junction safe)
│   │   ├── MemoryOptimizer.cs      # Working set trim & CLR GC collector
│   │   ├── SnapshotExporter.cs     # System diagnostic report generator
│   │   ├── ConfigManager.cs        # Persistent settings in %APPDATA%
│   │   └── ToolLauncher.cs         # Safe Windows diagnostic launchers
│   ├── Models/
│   │   ├── SystemMetrics.cs        # Strongly typed telemetry DTOs & process models
│   │   └── AiAgentSession.cs       # AI Agent & MCP session and subprocess hierarchy models
│   ├── Modules/
│   │   ├── CpuCollector.cs         # GetSystemTimes P/Invoke delta math
│   │   ├── MemoryCollector.cs      # GlobalMemoryStatusEx RAM & PageFile
│   │   ├── DiskCollector.cs        # DriveInfo multi-volume evaluator
│   │   ├── NetworkCollector.cs     # NetworkInterface live Rx/Tx & ICMP Ping
│   │   ├── ProcessCollector.cs     # Thread-safe debounced process sampler (_syncLock + fast sorting)
│   │   ├── AiAgentCollector.cs     # Toolhelp32 process tree scanner & MCP session aggregator
│   │   ├── ServiceCollector.cs     # Windows ServiceController census
│   │   ├── HardwareCollector.cs    # Battery status, uptime, OS/CPU/GPU specs
│   │   └── StartupCollector.cs     # Registry & Startup folder enumerator
│   └── UI/
│       ├── MainWindow.xaml & .cs   # Interactive HUD, Bento grid, AI Agents Tab, WM_GETMINMAXINFO hook
│       ├── ProcessDetailsWindow.xaml & .cs # 360° modal process inspector dialog
│       └── Themes/                 # Dynamic Pastel Dark, Light, Neon & Rose palettes + CommonStyles
├── scripts/
│   └── Build-Package.ps1           # MSBuild dynamic resolver & packaging pipeline
├── tests/
│   ├── Metrics.Tests.ps1           # 15-Test Health & Reflection validation suite
│   └── DeepStress.Tests.ps1        # 5-Test Live Process Tree, Handle Leak & 5s Smoke suite
└── releases/                       # Standalone .exe, Setup installer & Portable ZIP
```

---

### 5. Setup & Build Instructions

#### Direct Launch:
Run the compiled standalone executable inside `releases/`:
```powershell
.\releases\SimplePCMonitor.exe
```

#### Build from Source:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Package.ps1 -Version "v2.4.0"
```

#### Run Automated Health & Stress Tests (20 Tests):
```powershell
# 1. Health and Type Tests (15 tests)
powershell -ExecutionPolicy Bypass -File .\tests\Metrics.Tests.ps1

# 2. Deep Stress, Handle Leaks & Smoke Tests (5 tests)
powershell -ExecutionPolicy Bypass -File .\tests\DeepStress.Tests.ps1
```

---

### 6. Key Learnings & Engineering Takeaways
1. **AI Agent & MCP Process Tree Discovery & Subprocess Classification:** Using `CreateToolhelp32Snapshot` enables atomic process hierarchy mapping in $<0.8\text{ ms}$. Accurately classifying child processes requires inspecting command-line markers (`--stdio`, `mcp-remote`, `modelcontextprotocol`, `/mcp`) rather than relying on host runtimes (`node.exe`, `python.exe`), enabling discovery of compiled Go and Rust MCP servers while discarding generic build tools or language servers. Separating total child processes (`ChildProcessCount`) from verified MCP servers (`McpServersCount`), isolating ephemeral package runners (`npx`, `uvx`), promoting nested CLI agent sessions to their own roots with session-boundary tree pruning, wrapping native process handles in deterministic `SafeProcessHandle` disposals (`using`/`Dispose()`), and guarding time-series CPU deltas with re-entrancy locks (`_sampleGate`) eliminates handle table exhaustion and metric duplication across parent IDEs and child sessions.
2. **Triple PID Reuse Mitigation Gate:** Windows reassigns PIDs rapidly upon process termination. Storing and verifying `child.StartTime >= parent.StartTime.AddSeconds(-2)` prevents false-positive parent-child associations when examining long-running developer sessions.
3. **Two-Phase Termination & Reverse Topological Tree Termination:** When closing complex applications, Phase 1 dispatches non-blocking graceful close signals (`CloseMainWindow` / `WM_CLOSE` / `AttachConsole` + `CTRL_C_EVENT`) and detects if the window minimized to the System Tray (`MainWindowHandle == IntPtr.Zero`). Escalating to Phase 2 terminates child MCP leaves before root CLI orchestrators, eliminating orphaned background processes and locked ports.
4. **Anti-Reparse Point / Junction Security:** Traditional recursive directory cleaners follow NTFS Junction Points and Symlinks into user data folders. Validating `FileAttributes.ReparsePoint` and enforcing dual timestamps (`CreationTime` + `LastWriteTime`) provides absolute sandbox isolation.
5. **Kernel-Level Thread Suspension (`ntdll.dll`):** Invoking `NtSuspendProcess` and `NtResumeProcess` directly allows freezing resource-hogging background tasks without corrupting their state or losing application sessions.
6. **Seamless Multi-Monitor Window Maximization (`WM_GETMINMAXINFO`):** Handling Win32 `0x0024` and extracting per-monitor work area dimensions via `MonitorFromWindow` eliminates window clipping across high-DPI and multi-monitor setups.
7. **Resilient Crash Trapping Architecture (`CrashLogger.cs`):** Multi-tier exception hooking across `AppDomain`, `TaskScheduler`, and `Dispatcher` with 1MB size caps and 5-log/10s rate limiting prevents diagnostic spam and application crashes from unobserved background threads.
8. **Resilient Session Context & Cache Eviction Safeguards:** Headless or resumed autonomous CLI agents frequently lack window titles; Simple PC Monitor resolves these via regex CLI inspection (`--resume=`) into compact session hashes (`🔗 Sesión <8-char-hash>`) and extracts active AI models (`--model`) displayed as `🧬 <ModelName>`. In WPF XAML, dual null-and-empty `DataTriggers` (`Value=""` and `Value="{x:Null}"`) guarantee seamless visual collapse when no model flag exists. Crucially, cache eviction passes across CPU delta histories (`_prevCpuSamples`), resolved session metadata, and UI collapse states (`CollapsedSessionPids`) are protected behind an `allRunningPids.Count > 0` boundary check, preventing catastrophic cache purges if a transient OS snapshot call fails under high resource contention.

---

## 🇪🇸 Español

### 1. Descripción del Proyecto
**Simple PC Monitor v2.4.0** es un centro de mando interactivo y panel de telemetría de alto rendimiento desarrollado exclusivamente en C# compilado y Windows Presentation Foundation (WPF). Monitorea y gestiona de forma activa los recursos críticos del sistema—**CPU, Memoria RAM, Almacenamiento Multidisco, Red y Latencia Ping, Procesos en Tiempo Real, Sesiones de Agentes de IA y Servidores MCP, Servicios de Windows, Tareas Programadas, Programas de Inicio y Aceleradores de Hardware (GPU/NPU)**—en un único ejecutable standalone de **585 KB** sin dependencias externas.

---

### 2. ⚡ Guía de Botones de Acción y Centro de Mando

Simple PC Monitor evoluciona de un monitor pasivo a un **Centro de Mando Activo**. A continuación se detalla el comportamiento exacto y la API nativa detrás de cada control interactivo:

| Botón / Acción | Ubicación en UI | API Win32 / Kernel Utilizada | Comportamiento y Propósito Exacto |
| :--- | :--- | :--- | :--- |
| **🚀 Modo Turbo** | Ribbon Superior / Bandeja | Win32 `PowrProf.dll` (`PowerSetActiveScheme`) + `EmptyWorkingSet` | Activa al instante el plan de energía de **Alto Rendimiento** de Windows (desestaciona núcleos de CPU) y ejecuta simultáneamente una purga agresiva del *working set* de memoria RAM en procesos de usuario. |
| **🤖 Monitor de Agentes IA & MCP** | Pestaña Agentes IA | Win32 `CreateToolhelp32Snapshot` + `SafeProcessHandle` + `_sampleGate` + Guarda PID Reuse + Resiliencia Snapshot | Detecta herramientas CLI de IA (`claude.exe`, `gemini.exe`, `codex.exe`, `aider.exe`, `ollama.exe`, `cursor.exe`, `antigravity.exe`), identifica sesiones CLI reanudadas por hash/UUID (`--resume=`, `🔗 Sesión <8-char-hash>`), visualiza badges dinámicos del Modelo de IA (`--model`, `🧬 <ModelName>`), desacopla subprocesos totales (`ChildProcessCount`) de servidores MCP verificados (`McpServersCount`), aísla lanzadores efímeros (`npx`, `uvx`), detecta servidores MCP compilados en Go/Rust por flags CLI, promueve sesiones CLI independientes con corte de fronteras en el árbol, blinda el vaciado de cachés ante fallos de snapshot (`allRunningPids.Count > 0`), y refleja dinámicamente estados Activo (Esmeralda `#10B981`) vs Inactivo (Pizarra `#64748B`). |
| **🛑 Cierre Ordenado en Dos Fases** | Pestañas Procesos e IA | `CloseMainWindow` / `WM_CLOSE` + Detección Tray | **Fase 1**: Envío no bloqueante de solicitud de cierre ordenado y detección inteligente de minimizado a la Bandeja del Sistema (`MainWindowHandle == IntPtr.Zero`). **Fase 2**: Confirmación para forzar cierre solo si continúa activo o colgado. |
| **⚡ Terminar Árbol (Tree Kill)** | Pestaña Agentes IA | Terminación Topológica Inversa | Finaliza árboles de procesos completos en orden topológico inverso (subprocesos MCP primero $\rightarrow$ proceso raíz al final), evitando procesos huérfanos zombis. |
| **🌐 Vaciar DNS** | Ribbon Superior | Nativa `dnsapi.dll` (`DnsFlushResolverCache`) | Purga y reinicia la caché del solucionador de nombres DNS de Windows en 0.01 ms, corrigiendo errores de navegación y resolución de dominios sin abrir CMD. |
| **🧹 Limpiar Temporales** | Ribbon Superior | Multizona `SafeTempCleaner` (>24h Cutoff) | Limpieza segura de archivos temporales en `%TEMP%`, `C:\Windows\Temp`, `WinSxS\Temp`, `SoftwareDistribution\Download` y `DeliveryOptimization`. Blindado con **aislamiento de Junctions/Symlinks** y **Guarda de Doble Marca de Tiempo** (`CreationTime` + `LastWriteTime`). |
| **⚠️ Rescatar Proceso** | Alerta en Barra de Título | Watchdog Win32 `IsResponding` + Cierre en Dos Fases | Detecta en vivo procesos con ventanas que no responden a la cola de mensajes de Windows (`IsResponding == false`). El botón *"Rescatar"* ejecuta el protocolo seguro de cierre en dos fases. |
| **⏸️ Suspender Proceso** | Lista de Procesos / Contexto | Kernel `ntdll.dll` (`NtSuspendProcess`) | Congela todos los hilos de ejecución de un proceso desbocado, reduciendo su consumo de CPU al 0.0% instantáneamente sin cerrarlo ni perder el trabajo abierto. |
| **▶️ Resume Process** | Lista de Procesos / Contexto | Kernel `ntdll.dll` (`NtResumeProcess`) | Reactiva un proceso previamente suspendido, devolviendo sus hilos al planificador de tareas de Windows. |
| **🚨 Reanudar Todos** | Barra de Pestaña Procesos | Batch `NtResumeProcess` Watchdog | Botón de seguridad global que descongela simultáneamente todos los procesos de usuario suspendidos. |
| **⚡ Prioridad de CPU** | Menú Contextual | Win32 `ProcessPriorityClass` (`SetPriorityClass`) | Modifica la prioridad del proceso en el planificador del procesador (`Tiempo Real`, `Alta`, `Normal`, `Baja`, `Inactiva`) para priorizar juegos o renderizados. |
| **🔍 Búsqueda en Vivo** | Barra de Pestaña Procesos | Filtro Reactivo con Debounce de 200 ms | Filtra al instante por ejecutable, PID o nombre comercial de la aplicación sin congelar la interfaz. |
| **⚡ Ordenación Rápida en Memoria** | Barra de Pestaña Procesos | `ApplyProcessSortingFast` + `_syncLock` | Alterna instantáneamente entre orden descendente por CPU % y RAM (MB) en memoria sin bloquear la interfaz ni repetir escaneos del sistema operativo. |
| **🎛️ Tarjetas Bento Clicables** | Panel Principal (HUD) | Enrutamiento de Eventos WPF | Al hacer clic en cualquier tarjeta (CPU, RAM, GPU, Disco, Red) redirige a la pestaña de detalle y aplica el filtro relevante. |
| **⚡ Optimizar RAM** | Tarjeta Memoria / Bandeja | Win32 `SetProcessWorkingSetSize` / `EmptyWorkingSet` | Vierte las páginas de memoria no referenciadas del proceso físico al archivo de paginación y ejecuta recolección de basura CLR. |
| **📌 Fijar Ventana (Pin)** | Barra de Título | Propiedad `Topmost` de WPF | Mantiene la ventana por encima de juegos o aplicaciones a pantalla completa para monitorización continua. |
| **🗖 Maximizado Preciso** | Controles de Ventana | Hook Nativo Win32 `WM_GETMINMAXINFO` | Intercepta el mensaje `0x0024` y calcula el área de trabajo mediante `MonitorFromWindow`, evitando que la ventana tape la barra de tareas o se desborde en múltiples pantallas. |
| **🎨 4 Temas Visuales** | Paleta en Barra de Título | `ActivePillActionButtonStyle` y Recursos Dinámicos | Alterna al instante entre **Pastel Oscuro**, **Pastel Claro**, **Cyberpunk Neón** y **Sakura Rosa** con botones vectoriales de estilo adaptativo. |

> [!TIP]
> 📖 **Manual Técnico y de Seguridad en Detalle:** Si deseas consultar los diagramas de secuencia Mermaid, invariantes de llamadas nativas P/Invoke y el blindaje TOCTOU/Junctions, revisa el **[Manual Técnico del Centro de Mando](docs/command-center-guide.md)**.

---

### 3. Características Nativas Destacadas:
- **🤖 Monitor de Agentes IA & Servidores MCP:** Detección en tiempo real de sesiones de herramientas de IA CLI y subprocesos MCP hijos con métricas desacopladas, identificación de sesiones reanudadas por hash (`--resume=`, `🔗 Sesión <8-char-hash>`), badge condicional de modelo (`--model`, `🧬 <ModelName>`) con DataTriggers resistentes a null/cadenas vacías, blindaje de vaciado de caché Toolhelp32 (`allRunningPids.Count > 0`), tolerancia a cold-start de PEB y badges dinámicos de estado Activo/Idle.
- **⚡ Arquitectura Cero Fugas de Handles y Cerrojo Anti-Reentrada:** Disposición determinista de handles Win32 (`SafeProcessHandle` mediante `using`/`Dispose()`) y cerrojo anti-reentrada `_sampleGate` para cálculo estable de deltas de CPU bajo alta frecuencia de muestreo.
- **🛡️ Protocolo de Cierre en Dos Fases:** Cierre seguro y no bloqueante con detección de aplicaciones minimizadas a la Bandeja del Sistema (*System Tray*) y terminación topológica inversa.
- **Diseño Bento a Ancho Completo:** Cuadrícula de alta densidad sin márgenes muertos, optimizada para resoluciones modernas.
- **Registro Resiliente de Fallos (`CrashLogger.cs`):** Captura global de excepciones en `AppDomain`, `TaskScheduler` y `Dispatcher` con rotación automática a 1MB y límite de tasa de 5 registros cada 10 segundos.
- **Centro de Almacenamiento Multidisco:** Visualizador en tiempo real de unidades de disco (NVMe/SSD/HDD), estado de salud, espacio libre y accesos directos al Explorador de archivos.
- **🔍 Inspector 360° de Procesos:** Identificación amigable de nombres comerciales, publicadores certificados, arquitectura y memoria con 0ms de retardo.
- **Lista Negra de Protección del Sistema:** Protección estricta que previene la suspensión o cierre de procesos vitales del sistema (`csrss`, `dwm`, `svchost`, `explorer`, `services`, `lsass`).
- **Pipeline de CI/CD Automatizado:** Compilación y ejecución de 20 tests automatizados de salud, estrés en vivo y arquitectura en cada release.

---

### 4. Instrucciones de Compilación y Ejecución

#### Ejecutar Directamente:
```powershell
.\releases\SimplePCMonitor.exe
```

#### Compilar desde el Código Fuente:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Package.ps1 -Version "v2.4.0"
```

#### Ejecutar Pruebas Automatizadas (20 Tests):
```powershell
# 1. Pruebas de Salud y Arquitectura (15 tests)
powershell -ExecutionPolicy Bypass -File .\tests\Metrics.Tests.ps1

# 2. Pruebas de Estrés en Vivo, Fugas de Handles y Smoke (5 tests)
powershell -ExecutionPolicy Bypass -File .\tests\DeepStress.Tests.ps1
```

---

## 📄 License
MIT License. Free for personal and commercial use.
