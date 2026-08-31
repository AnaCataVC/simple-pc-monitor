# Simple PC Monitor 🖥️⚡

<div align="center">

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=flat-square&logo=windows)](https://www.microsoft.com/windows)
[![C# .NET](https://img.shields.io/badge/C%23-WPF%20%2F%20XAML-512BD4?style=flat-square&logo=csharp)](https://dotnet.microsoft.com/)
[![Version](https://img.shields.io/badge/Release-v2.0.1-93A8FD?style=flat-square)](https://github.com/AnaCataVC/simple-pc-monitor/releases/tag/v2.0.1)
[![Binary Size](https://img.shields.io/badge/Binary%20Size-585%20KB-success?style=flat-square)]()
[![Tests](https://img.shields.io/badge/Tests-13%20Passed-brightgreen?style=flat-square)]()
[![Antivirus](https://img.shields.io/badge/Antivirus-0%20False%20Positives-7EE7B8?style=flat-square)]()
[![License](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)

*A high-performance, lightweight, and interactive Windows desktop command center engineered in compiled Native C# (.NET WPF/XAML). Features zero external dependencies, sub-millisecond Win32 P/Invoke telemetry, kernel-level process control (NtSuspend/NtResume), 1-click power plans, multizone hardened storage cleaning, interactive Bento metric cards, responsive multi-drive analytics, enterprise crash logging, seamless multi-monitor DPI maximization, and zero-heuristic footprint in a standalone 585 KB binary.*

[English](#-english) • [Español](#-español)

</div>

---

## 🇺🇸 English

### 1. Project Description
**Simple PC Monitor v2.0.1** is an interactive desktop command center and telemetry suite built exclusively with compiled C# and Windows Presentation Foundation (WPF). It monitors and actively manages critical system resources—**CPU, Memory (RAM), Multi-Drive Storage, Network Latency & Throughput, Real-Time Processes, Windows Services, Scheduled Tasks, Startup Applications, and Hardware Accelerators (GPU/NPU)**—packaged into a single standalone `.exe` without third-party runtimes or background services.

---

### 2. ⚡ Command Center & Action Buttons Reference

Simple PC Monitor transitions from a passive observer to an **Active Command Center**. Below is the exact behavior and Win32/Kernel API mechanism behind every interactive control in the HUD:

| Action / Button | UI Location | Mechanism & Native Win32 / Kernel API | Exact Behavior & Purpose |
| :--- | :--- | :--- | :--- |
| **🚀 Turbo Mode** | Top Ribbon / Tray | Win32 `PowrProf.dll` (`PowerSetActiveScheme`) + `EmptyWorkingSet` | Instantly switches the Windows power plan to **High Performance** (unparking CPU cores) and concurrently purges idle working set memory pages across user processes to reclaim physical RAM. |
| **🌐 Flush DNS** | Top Ribbon | Native `dnsapi.dll` (`DnsFlushResolverCache`) | Directly purges and resets the Windows DNS name resolver cache in 0.01 ms, resolving stale routes, domain lookup glitches, and network timeouts without needing CMD. |
| **🧹 Clean Temp** | Top Ribbon | Multizone `SafeTempCleaner` (>24h Cutoff) | Safely cleans obsolete cache files in `%TEMP%`, `C:\Windows\Temp`, `WinSxS\Temp`, `SoftwareDistribution\Download`, and `DeliveryOptimization`. Protected by **NTFS Reparse Point (Junction/Symlink) isolation** and **Dual Timestamp Gate** (`CreationTime` + `LastWriteTime`). |
| **⚠️ Rescue Process** | Dynamic Title Alert | Win32 `IsResponding` Watchdog + `Process.Kill()` | Real-time watchdog detects windowed processes that stop responding to the Windows message loop (`IsResponding == false`). Clicking *"Rescue"* terminates the frozen application gracefully. |
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
│   │   ├── NativeMethods.cs        # Win32 & NT kernel P/Invoke (ntdll, user32, dnsapi, powrprof)
│   │   ├── CrashLogger.cs          # Resilient crash logging (1MB cap, rotation, rate limiting)
│   │   ├── PowerPlanManager.cs     # Native Win32 power scheme switcher
│   │   ├── ProcessManager.cs       # Kernel process suspend/resume & blacklist guards
│   │   ├── ProcessMetadataCache.cs # High-performance 0ms metadata caching
│   │   ├── SafeTempCleaner.cs      # Hardened multizone storage cleaner (Anti-TOCTOU & Junction safe)
│   │   ├── MemoryOptimizer.cs      # Working set trim & CLR GC collector
│   │   ├── SnapshotExporter.cs     # System diagnostic report generator
│   │   ├── ConfigManager.cs        # Persistent settings in %APPDATA%
│   │   └── ToolLauncher.cs         # Safe Windows diagnostic launchers
│   ├── Models/
│   │   └── SystemMetrics.cs        # Strongly typed telemetry DTOs & process models
│   ├── Modules/
│   │   ├── CpuCollector.cs         # GetSystemTimes P/Invoke delta math
│   │   ├── MemoryCollector.cs      # GlobalMemoryStatusEx RAM & PageFile
│   │   ├── DiskCollector.cs        # DriveInfo multi-volume evaluator
│   │   ├── NetworkCollector.cs     # NetworkInterface live Rx/Tx & ICMP Ping
│   │   ├── ProcessCollector.cs     # Thread-safe debounced process sampler (_syncLock + fast sorting)
│   │   ├── ServiceCollector.cs     # Windows ServiceController census
│   │   ├── HardwareCollector.cs    # Battery status, uptime, OS/CPU/GPU specs
│   │   └── StartupCollector.cs     # Registry & Startup folder enumerator
│   └── UI/
│       ├── MainWindow.xaml & .cs   # Interactive HUD, Bento grid, WM_GETMINMAXINFO hook
│       ├── ProcessDetailsWindow.xaml & .cs # 360° modal process inspector dialog
│       └── Themes/                 # Dynamic Pastel Dark, Light, Neon & Rose palettes + CommonStyles
├── scripts/
│   └── Build-Package.ps1           # MSBuild dynamic resolver & packaging pipeline
├── tests/
│   └── Metrics.Tests.ps1           # 13-Test Pester automated validation suite
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
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Package.ps1 -Version "v2.0.0"
```

#### Run Automated Health Tests (13 Tests):
```powershell
powershell -ExecutionPolicy Bypass -File .\tests\Metrics.Tests.ps1
```

---

### 6. Key Learnings & Engineering Takeaways
1. **Kernel-Level Thread Suspension (`ntdll.dll`):** Invoking `NtSuspendProcess` and `NtResumeProcess` directly allows freezing resource-hogging background tasks without corrupting their state or losing application sessions.
2. **Anti-Reparse Point / Junction Security:** Traditional recursive directory cleaners follow NTFS Junction Points and Symlinks into user data folders. Validating `FileAttributes.ReparsePoint` and enforcing dual timestamps (`CreationTime` + `LastWriteTime`) provides absolute sandbox isolation.
3. **P/Invoke Power Scheme Switching:** Native `PowrProf.dll` (`PowerSetActiveScheme`) enables sub-millisecond power profile changes without requiring administrator elevation.
4. **Non-Blocking Process Concurrency & In-Memory Sorting:** Thread-safe `_syncLock` synchronizes CPU delta samples across async sampling cycles, while `ApplyProcessSortingFast` re-sorts cached Linq snapshots without triggering synchronous Win32 process enumeration.
5. **Seamless Multi-Monitor Window Maximization (`WM_GETMINMAXINFO`):** Handling Win32 `0x0024` and extracting per-monitor work area dimensions via `MonitorFromWindow` eliminates window clipping across high-DPI and multi-monitor setups.
6. **Resilient Crash Trapping Architecture (`CrashLogger.cs`):** Multi-tier exception hooking across `AppDomain`, `TaskScheduler`, and `Dispatcher` with 1MB size caps and 5-log/10s rate limiting prevents diagnostic spam and application crashes from unobserved background threads.

---

## 🇪🇸 Español

### 1. Descripción del Proyecto
**Simple PC Monitor v2.0.1** es un centro de mando interactivo y panel de telemetría de alto rendimiento desarrollado exclusivamente en C# compilado y Windows Presentation Foundation (WPF). Monitorea y gestiona de forma activa los recursos críticos del sistema—**CPU, Memoria RAM, Almacenamiento Multidisco, Red y Latencia Ping, Procesos en Tiempo Real, Servicios de Windows, Tareas Programadas, Programas de Inicio y Aceleradores de Hardware (GPU/NPU)**—en un único ejecutable standalone de **585 KB** sin dependencias externas.

---

### 2. ⚡ Guía de Botones de Acción y Centro de Mando

Simple PC Monitor evoluciona de un monitor pasivo a un **Centro de Mando Activo**. A continuación se detalla el comportamiento exacto y la API nativa detrás de cada control interactivo:

| Botón / Acción | Ubicación en UI | API Win32 / Kernel Utilizada | Comportamiento y Propósito Exacto |
| :--- | :--- | :--- | :--- |
| **🚀 Modo Turbo** | Ribbon Superior / Bandeja | Win32 `PowrProf.dll` (`PowerSetActiveScheme`) + `EmptyWorkingSet` | Activa al instante el plan de energía de **Alto Rendimiento** de Windows (desestaciona núcleos de CPU) y ejecuta simultáneamente una purga agresiva del *working set* de memoria RAM en procesos de usuario. |
| **🌐 Vaciar DNS** | Ribbon Superior | Nativa `dnsapi.dll` (`DnsFlushResolverCache`) | Purga y reinicia la caché del solucionador de nombres DNS de Windows en 0.01 ms, corrigiendo errores de navegación y resolución de dominios sin abrir CMD. |
| **🧹 Limpiar Temporales** | Ribbon Superior | Multizona `SafeTempCleaner` (>24h Cutoff) | Limpieza segura de archivos temporales en `%TEMP%`, `C:\Windows\Temp`, `WinSxS\Temp`, `SoftwareDistribution\Download` y `DeliveryOptimization`. Blindado con **aislamiento de Junctions/Symlinks** y **Guarda de Doble Marca de Tiempo** (`CreationTime` + `LastWriteTime`). |
| **⚠️ Rescatar Proceso** | Alerta en Barra de Título | Watchdog Win32 `IsResponding` + `Process.Kill()` | Detecta en vivo procesos con ventanas que no responden a la cola de mensajes de Windows (`IsResponding == false`). El botón *"Rescatar"* permite finalizar la aplicación colgada de inmediato. |
| **⏸️ Suspender Proceso** | Lista de Procesos / Contexto | Kernel `ntdll.dll` (`NtSuspendProcess`) | Congela todos los hilos de ejecución de un proceso desbocado, reduciendo su consumo de CPU al 0.0% instantáneamente sin cerrarlo ni perder el trabajo abierto. |
| **▶️ Reanudar Proceso** | Lista de Procesos / Contexto | Kernel `ntdll.dll` (`NtResumeProcess`) | Reactiva un proceso previamente suspendido, devolviendo sus hilos al planificador de tareas de Windows. |
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
- **Diseño Bento a Ancho Completo:** Cuadrícula de alta densidad sin márgenes muertos, optimizada para resoluciones modernas.
- **Registro Resiliente de Fallos (`CrashLogger.cs`):** Captura global de excepciones en `AppDomain`, `TaskScheduler` y `Dispatcher` con rotación automática a 1MB y límite de tasa de 5 registros cada 10 segundos.
- **Centro de Almacenamiento Multidisco:** Visualizador en tiempo real de unidades de disco (NVMe/SSD/HDD), estado de salud, espacio libre y accesos directos al Explorador de archivos.
- **🔍 Inspector 360° de Procesos:** Identificación amigable de nombres comerciales, publicadores certificados, arquitectura y memoria con 0ms de retardo.
- **Lista Negra de Protección del Sistema:** Protección estricta que previene la suspensión o cierre de procesos vitales del sistema (`csrss`, `dwm`, `svchost`, `explorer`, `services`, `lsass`).
- **Pipeline de CI/CD Automatizado:** Compilación y ejecución de 13 tests automatizados de salud y arquitectura en cada release.

---

### 4. Instrucciones de Compilación y Ejecución

#### Ejecutar Directamente:
```powershell
.\releases\SimplePCMonitor.exe
```

#### Compilar desde el Código Fuente:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Package.ps1 -Version "v2.0.1"
```

#### Ejecutar Pruebas Automatizadas (13 Tests):
```powershell
powershell -ExecutionPolicy Bypass -File .\tests\Metrics.Tests.ps1
```

---

## 📄 License
MIT License. Free for personal and commercial use.
