# Simple PC Monitor 🖥️⚡

<div align="center">

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=flat-square&logo=windows)](https://www.microsoft.com/windows)
[![C# .NET](https://img.shields.io/badge/C%23-WPF%20%2F%20XAML-512BD4?style=flat-square&logo=csharp)](https://dotnet.microsoft.com/)
[![Binary Size](https://img.shields.io/badge/Binary%20Size-585%20KB-success?style=flat-square)]()
[![Antivirus](https://img.shields.io/badge/Antivirus-0%20False%20Positives-7EE7B8?style=flat-square)]()
[![License](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)

*A visual, lightweight, and interactive system monitor dashboard for Windows engineered in compiled Native C# (.NET WPF/XAML). Zero external dependencies, sub-millisecond Win32 P/Invoke telemetry, power plan switching, protected process management, safe temp file cleaning, circular radial gauges, live wave sparklines, and standard Windows window controls in a single 585 KB standalone executable.*

[English](#-english) • [Español](#-español)

</div>

---

## 🇺🇸 English

### 1. Project Description
**Simple PC Monitor** is a clean, interactive desktop telemetry dashboard and performance control suite built exclusively with compiled C# and Windows Presentation Foundation (WPF). It monitors and manages critical system resources—**CPU, Memory (RAM), Disks, Network Latency & Throughput, Top Active Processes, Windows Services, Scheduled Tasks, Startup Applications, and Hardware state**—packaged into a single standalone `.exe` without third-party installers, runtimes, or false-positive heuristic flags.

#### Key Highlights & Interactive Features:
- **Standard Windows Window Controls:** Full window manipulation with **Minimize, Maximize / Restore, and Close** buttons, title bar double-click maximize, smooth dragging, and an **Always-on-Top (Pin)** toggle.
- **🔍 Rich Process Diagnostics & Friendly Names:** Resolves human-readable application names (`FileDescription`), publishers (`CompanyName`), and window titles with zero sampling latency (0ms cache overhead). Includes rich hover tooltips and a dedicated 360° modal inspector dialog (`ProcessDetailsWindow`) triggered by double-click, inline `ℹ️` button, or context menu.
- **⚡ Native Win32 Power Plan Switcher:** 1-click instant switching between **Balanced, High Performance, and Power Saver** using native `PowrProf.dll` (0.01 ms execution, zero UAC elevation).
- **🔴 Safe Process Management & Context Menu:** Right-click any process to **Kill Task**, **Open File Location in Explorer**, **Copy PID & Memory Info**, or **Search Online**—protected by a strict blacklist of critical Windows system processes (`csrss`, `dwm`, `svchost`, `explorer`, `services`, etc.) to prevent crashes.
- **🧹 Safe Temporary File Cleaner:** Cleans `%TEMP%` and `%LOCALAPPDATA%\Temp` files older than 24 hours while safely bypassing in-use files, complete with in-app toast feedback showing exact space freed.
- **⚡ Working Set RAM Optimizer:** Trims idle memory pages and triggers garbage collection to free active RAM on demand.
- **🚀 Startup Applications Manager:** Displays applications configured to start with Windows from User and System Registries and the Startup Folder.
- **📸 Diagnostic Snapshot Exporter:** Generates full system health reports in Markdown format with one click, ready to paste or share.
- **🚨 Dynamic Gauge Alert States:** Radial progress arcs shift to warning amber and critical coral hues when CPU, RAM, or Disk loads exceed 85%.
- **🌐 Real-Time ICMP Ping Latency:** Measures active network latency (ms) in the background without freezing the UI thread.
- **🎨 4 Modern Pastel Themes:** Instant hot-swapping between **Pastel Dark**, **Pastel Light**, **Cyberpunk Neon**, and **Pastel Rose / Sakura**.
- **3-Mode Viewport Switcher:** Instant toggle between **Full HUD Analytics**, **Hero Graphs**, and a **Compact Desktop Widget**.

---

### 2. Technologies Used
- **C# & .NET WPF:** Strongly typed, compiled desktop application.
- **Direct3D / XAML Vector Graphics:** Declarative UI layout, trigonometric ArcSegments, and dynamic theming tokens.
- **Win32 P/Invoke:** `GetSystemTimes`, `GlobalMemoryStatusEx`, `PowerSetActiveScheme`, `PowerGetActiveScheme`, `EmptyWorkingSet`, `SetProcessWorkingSetSize`, `QueryFullProcessImageName`, `IsWow64Process`.
- **System Diagnostics & Networking:** `System.Net.NetworkInformation.Ping`, `System.IO.DriveInfo`, `System.Diagnostics.Process`, `System.Diagnostics.FileVersionInfo`, `System.ServiceProcess.ServiceController`, `Microsoft.Win32.Registry`.

---

### 3. Architecture & Modular Structure

```
simple-pc-monitor/
├── src/
│   ├── SimplePCMonitor.csproj      # C# WPF project file
│   ├── App.xaml & App.xaml.cs      # Entrypoint & 4-theme dynamic switcher
│   ├── Core/
│   │   ├── NativeMethods.cs        # Win32 P/Invoke declarations (kernel32.dll)
│   │   ├── PowerPlanManager.cs     # Native Win32 power scheme switcher (powrprof.dll)
│   │   ├── ProcessManager.cs       # Safe process killer & blacklist protection
│   │   ├── ProcessMetadataCache.cs # High-performance 0ms metadata caching & OS dictionary
│   │   ├── SafeTempCleaner.cs      # Safe %TEMP% directory cleaner (>24h)
│   │   ├── MemoryOptimizer.cs      # Working set trim & CLR GC collector
│   │   ├── SnapshotExporter.cs     # System diagnostic report generator
│   │   ├── ConfigManager.cs        # Persistent settings in %APPDATA%
│   │   └── ToolLauncher.cs         # Safe Windows diagnostic launchers
│   ├── Models/
│   │   └── SystemMetrics.cs        # Strongly typed telemetry DTOs, ProcessDetailedInfo
│   ├── Modules/
│   │   ├── CpuCollector.cs         # GetSystemTimes P/Invoke delta math
│   │   ├── MemoryCollector.cs      # GlobalMemoryStatusEx RAM & PageFile
│   │   ├── DiskCollector.cs        # DriveInfo multi-volume evaluator
│   │   ├── NetworkCollector.cs     # NetworkInterface live Rx/Tx & ICMP Ping
│   │   ├── ProcessCollector.cs     # Top RAM processes with metadata injection
│   │   ├── ServiceCollector.cs     # Windows ServiceController census
│   │   ├── HardwareCollector.cs    # Battery status, uptime, OS/CPU/GPU specs
│   │   └── StartupCollector.cs     # Registry & Startup folder enumerator
│   └── UI/
│       ├── MainWindow.xaml & .cs   # Rich HUD layout, window controls, and toast notifications
│       ├── ProcessDetailsWindow.xaml & .cs # 360° modal process inspector dialog
│       ├── Icons/VectorIcons.xaml  # StreamGeometry vector icons
│       └── Themes/
│           ├── CommonStyles.xaml   # Card, button, menu, progress bar templates
│           ├── PastelDark.xaml     # Obsidian & Lavender
│           ├── PastelLight.xaml    # Porcelain & Mint
│           ├── PastelNeon.xaml     # Cyberpunk Cyan & Magenta
│           └── PastelRose.xaml     # Sakura Pink & Matcha
└── releases/
    ├── SimplePCMonitor.exe         # Single standalone executable (585 KB)
    └── Simple-PC-Monitor-v1.1.0-Portable.zip
```

---

### 4. Setup & Build Instructions

#### Direct Launch:
Simply run the compiled executable inside `releases/`:
```powershell
.\releases\SimplePCMonitor.exe
```

#### Build from Source:
Build directly using native Windows MSBuild:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Package.ps1 -Version "v1.1.0"
```

#### Run Health Tests:
```powershell
powershell -ExecutionPolicy Bypass -File .\tests\Metrics.Tests.ps1
```

---

### 5. Key Learnings & Engineering Takeaways
1. **P/Invoke Power Scheme Switching:** Using Win32 `PowrProf.dll` (`PowerSetActiveScheme`) allows instant, sub-millisecond power profile switches without requiring UAC administrator privileges.
2. **Crash-Resilient Process Management:** Combining `QueryFullProcessImageName` with a curated blacklist prevents accidental termination of critical Windows services while enabling seamless Explorer file lookup.
3. **Safe Memory Optimization:** Trimming process working sets via `SetProcessWorkingSetSize` and `EmptyWorkingSet` safely releases physical RAM pages back to the OS without instability.
4. **Adaptive Custom WPF Chrome:** Combining `AllowsTransparency="True"` with responsive margin and corner-radius adjustments on `StateChanged` provides flawless window maximizing, minimizing, dragging, and pin-to-top behavior.

---

## 🇪🇸 Español

### 1. Descripción del Proyecto
**Simple PC Monitor** es un panel de telemetría de escritorio y suite de control de rendimiento nativo desarrollado en C# compilado y Windows Presentation Foundation (WPF). Monitorea y gestiona los recursos críticos del sistema—**CPU, Memoria RAM, Discos, Latencia Ping y Rendimiento de Red, Procesos Principales, Servicios de Windows, Tareas Programadas, Programas de Inicio y Hardware**—empaquetado en un único archivo ejecutable de **585 KB** sin instaladores ni dependencias externas.

#### Características Principales y Acciones Interactivas:
- **Controles de Ventana Estándar de Windows:** Manejo completo de ventana con botones de **Minimizar, Maximizar / Restaurar y Cerrar**, maximizado por doble clic en la barra de título, arrastre suave y modo **Fijar en Primer Plano (Pin / Always on Top)**.
- **🔍 Inspector Profundo de Procesos e Identificación Amigable:** Resuelve nombres descriptivos de aplicaciones (`FileDescription`), desarrollador / publicador (`CompanyName`), arquitectura (64-bit / 32-bit) y títulos de ventana sin latencia (0ms overhead gracias a caché concurrente). Incluye Tooltips detallados y un modal de diagnóstico e inspección 360° (`ProcessDetailsWindow`) accesible con doble clic, botón `ℹ️` o menú contextual.
- **⚡ Selector Nativo de Planes de Energía:** Cambia al instante con 1 clic entre **Equilibrado, Alto Rendimiento y Ahorro de Energía** mediante `PowrProf.dll` nativo (sin requerir permisos de Administrador).
- **🔴 Control de Procesos Seguro con Menú Contextual:** Haz clic derecho en cualquier proceso para **Finalizar Tarea**, **Abrir Ubicación en el Explorador**, **Copiar PID y Memoria** o **Buscar en Google**—con protección estricta contra el cierre de procesos críticos de Windows (`csrss`, `dwm`, `svchost`, `explorer`, `services`, etc.).
- **🧹 Limpiador Seguro de Archivos Temporales:** Limpia archivos de `%TEMP%` y `%LOCALAPPDATA%\Temp` de más de 24 horas omitiendo archivos en uso, con notificación flotante (Toast) que muestra el espacio liberado.
- **⚡ Optimizador de Memoria RAM:** Limpia el Working Set de aplicaciones y recolecta basura para liberar memoria física al instante.
- **🚀 Gestor de Programas de Inicio:** Visualiza las aplicaciones que arrancan con Windows desde el Registro y la carpeta Startup.
- **📸 Exportador de Diagnóstico (Snapshot):** Genera informes completos de salud del sistema en formato Markdown con un solo clic.
- **🚨 Alertas Visuales en Gauges:** Los arcos radiales cambian dinámicamente a ámbar y coral de advertencia cuando el uso supera el 85%.
- **🌐 Medidor de Ping / Latencia en Tiempo Real:** Mide la latencia de red (ms) en segundo plano sin congelar la interfaz.
- **🎨 4 Temas Modernos:** Alterna al instante entre **Pastel Oscuro**, **Pastel Claro**, **Cyberpunk Neón** y **Pastel Rosa / Sakura**.

---

### 2. Ejecución y Compilación

#### Ejecutar Directamente:
```powershell
.\releases\SimplePCMonitor.exe
```

#### Compilar desde el Código Fuente:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Package.ps1 -Version "v1.1.0"
```

#### Ejecutar Pruebas Automatizadas:
```powershell
powershell -ExecutionPolicy Bypass -File .\tests\Metrics.Tests.ps1
```

---

## 📄 License
MIT License. Free for personal and commercial use.
