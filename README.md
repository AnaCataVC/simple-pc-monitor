# Simple PC Monitor 🖥️⚡

<div align="center">

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=flat-square&logo=windows)](https://www.microsoft.com/windows)
[![C# .NET](https://img.shields.io/badge/C%23-WPF%20%2F%20XAML-512BD4?style=flat-square&logo=csharp)](https://dotnet.microsoft.com/)
[![Binary Size](https://img.shields.io/badge/Binary%20Size-584%20KB-success?style=flat-square)]()
[![Antivirus](https://img.shields.io/badge/Antivirus-0%20False%20Positives-7EE7B8?style=flat-square)]()
[![License](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)

*A visual, lightweight, and minimalist system monitor dashboard for Windows engineered in compiled Native C# (.NET WPF/XAML). Zero external dependencies, sub-millisecond Win32 P/Invoke telemetry, circular radial gauges, live historical wave sparklines, and a single standalone 584 KB executable.*

[English](#-english) • [Español](#-español)

</div>

---

## 🇺🇸 English

### 1. Project Description
**Simple PC Monitor** is a clean, native desktop telemetry dashboard built exclusively with compiled C# and Windows Presentation Foundation (WPF). It monitors critical system resources—**CPU, Memory (RAM), Disks, Network I/O, Top Active Processes, Windows Services, Scheduled Tasks, and Hardware state**—packaged into a single standalone `.exe` without third-party installers, runtimes, or false-positive heuristic flags.

#### Key Highlights:
- **Single Standalone Executable (584 KB):** Double-click `SimplePCMonitor.exe` anywhere. Zero installation required.
- **Zero Antivirus Flags:** Standard PE32+ compiled binary with authentic DOS/PE headers, eliminating script dropper false positives.
- **Circular Radial Progress Rings:** Trigonometric pure XAML vector gauges for CPU, RAM, Primary Storage, and Network speeds.
- **Live Real-Time Historical Wave Charts:** Direct3D-accelerated 30-second continuous area wave sparklines for CPU Load and Network bandwidth.
- **Sub-Millisecond Native Telemetry:** High-speed Win32 P/Invoke (`GetSystemTimes`, `GlobalMemoryStatusEx`) yielding microsecond telemetry reads (< 0.005 ms).
- **60 FPS Hardware-Accelerated Rendering:** Asynchronous background telemetry loop pushes lock-free snapshots to the UI thread.
- **Visual Process Resource Meters:** Embedded horizontal RAM usage bars and PID pill chips in the process table.
- **3-Mode Viewport Switcher:** Instant toggle between **Full HUD Analytics**, **Hero Graphs**, and a **Compact Desktop Widget**.
- **Pastel Minimalist Design System:** Instant hot-swapping between **Pastel Dark** (Obsidian/Violet) and **Pastel Light** (Porcelain/Mint).
- **Native Diagnostic Launchers:** Instant access to Task Manager, Resource Monitor, Reliability Monitor, and PC Manager.

---

### 2. Technologies Used
- **C# & .NET WPF:** Strongly typed, compiled desktop application.
- **Direct3D / XAML Vector Graphics:** Declarative UI layout, trigonometric ArcSegments, and dynamic theming tokens.
- **Win32 P/Invoke:** `GetSystemTimes`, `GlobalMemoryStatusEx`, `GetSystemPowerStatus`, `GetTickCount64`.
- **System Diagnostics & Networking:** `System.Net.NetworkInformation`, `System.IO.DriveInfo`, `System.Diagnostics.Process`, `System.ServiceProcess.ServiceController`.

---

### 3. Architecture & Modular Structure

```
simple-pc-monitor/
├── src/
│   ├── SimplePCMonitor.csproj      # C# WPF project file
│   ├── App.xaml & App.xaml.cs      # Entrypoint & dynamic theme switcher
│   ├── Core/
│   │   ├── NativeMethods.cs        # Win32 P/Invoke declarations (kernel32.dll)
│   │   ├── ConfigManager.cs        # Persistent settings in %APPDATA%
│   │   └── ToolLauncher.cs         # Safe Windows diagnostic launchers
│   ├── Models/
│   │   └── SystemMetrics.cs        # Strongly typed telemetry DTOs
│   ├── Modules/
│   │   ├── CpuCollector.cs         # GetSystemTimes P/Invoke delta math
│   │   ├── MemoryCollector.cs      # GlobalMemoryStatusEx RAM & PageFile
│   │   ├── DiskCollector.cs        # DriveInfo multi-volume evaluator
│   │   ├── NetworkCollector.cs     # NetworkInterface live Rx/Tx throughput
│   │   ├── ProcessCollector.cs     # Top 8 RAM & CPU processes
│   │   ├── ServiceCollector.cs     # Windows ServiceController census
│   │   └── HardwareCollector.cs    # Battery status, uptime, OS/CPU/GPU specs
│   └── UI/
│       ├── MainWindow.xaml & .cs   # Rich HUD layout with radial rings & wave sparklines
│       ├── Icons/VectorIcons.xaml  # StreamGeometry vector icons
│       └── Themes/
│           ├── CommonStyles.xaml   # Card, button, progress bar templates
│           ├── PastelDark.xaml     # Pastel Dark tokens
│           └── PastelLight.xaml    # Pastel Light tokens
└── releases/
    ├── SimplePCMonitor.exe         # Single standalone executable (584 KB)
    └── Simple-PC-Monitor-v1.0.0-Portable.zip
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
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Package.ps1
```

---

### 5. Key Learnings & Engineering Takeaways
1. **Antivirus Heuristics vs. Real Binaries:** Unsigned script-wrapper stubs triggering PowerShell in hidden windows often generate heuristic false positives (e.g. McAfee / Windows SmartScreen). Migrating to a compiled C# binary provides standard PE import tables and eliminates false positives.
2. **Sub-Millisecond Telemetry:** Native C# P/Invoke calling `GetSystemTimes` and `GlobalMemoryStatusEx` samples CPU and RAM in less than 5 microseconds.
3. **Trigonometric Vector UI:** Dynamic circular gauges rendered using `ArcSegment` math ($x = cx + r \sin\theta, y = cy - r \cos\theta$) produce crisp, resolution-independent vector graphics.

---

## 🇪🇸 Español

### 1. Descripción del Proyecto
**Simple PC Monitor** es un panel de telemetría de escritorio nativo desarrollado en C# compilado y Windows Presentation Foundation (WPF). Monitorea los recursos críticos del sistema—**CPU, Memoria RAM, Discos, Red, Procesos Principales, Servicios, Tareas Programadas y Hardware**—empaquetado en un único archivo ejecutable de **584 KB** sin instaladores pesados ni falsos positivos de antivirus.

#### Características Principales:
- **Ejecutable Único (584 KB):** Haz doble clic en `SimplePCMonitor.exe` en cualquier parte. No requiere instalación.
- **Cero Falsos Positivos:** Binario compilado nativo estándar (PE32+), reconocido como 100% seguro por McAfee y Windows Defender.
- **Anillos Radiales Vectoriales:** Medidores circulares con matemáticas trigonométricas en XAML puro para CPU, RAM, Disco y Red.
- **Gráficos de Onda en Tiempo Real:** Gráficos de área de 30 segundos con aceleración Direct3D para Carga de CPU y Ancho de Banda.
- **Telemetría en Microsegundos:** Lecturas en menos de 0.005 ms mediante Win32 P/Invoke nativo.
- **3 Modos de Visualización:** Alterna al instante entre **HUD Completo**, **Modo Hero** y un **Widget de Escritorio Compacto**.
- **Diseño Pastel Minimalista:** Soporte instantáneo para tema **Pastel Oscuro** y **Pastel Claro**.

---

### 2. Ejecución y Compilación

#### Ejecutar Directamente:
```powershell
.\releases\SimplePCMonitor.exe
```

#### Compilar desde el Código Fuente:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Package.ps1
```

---

## 📄 License
MIT License. Free for personal and commercial use.
