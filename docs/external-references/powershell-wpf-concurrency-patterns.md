# PowerShell WPF Concurrency & UI Rendering Patterns

**Author:** ami-tech-lead & ami-expert-council  
**Created:** 2026-08-18  
**Project:** simple-pc-monitor  
**Status:** Validated  

---

## 1. Concurrency Patterns Evaluation

### The Problem
Executing telemetry loops directly on the WPF UI thread via `DispatcherTimer` introduces micro-stutters and frame drops whenever a system API takes >16ms.

```
DispatcherTimer (On STA UI Thread)
----------------------------------------------------------------->
[Tick] -> [CIM/Counter Query (50-200ms)] -> [UI Updates]
          ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
          UI FROZEN: No Drag, No Animation, No Hover
```

### The Solution: Dual-Runspace Producer-Consumer Pattern
Using an isolated background MTA Runspace (`[runspacefactory]::CreateRunspace()`) decoupled from the STA UI thread:

```
[Background MTA Runspace (Telemetry Worker Loop)]
  -> Sub-ms P/Invoke Telemetry (< 2ms total)
  -> Builds immutable Snapshot DTO
  -> Pushes to UI via $window.Dispatcher.BeginInvoke([Action]{...}, Background)
  -> Sleeps for interval ($intervalMs)

[Foreground STA UI Thread (WPF Direct3D/Mica Canvas)]
  -> 60 FPS continuous rendering
  -> Instant hover states & window dragging
  -> Receives pre-cooked telemetry DTO and updates bound text/progress values
```

---

## 2. Dynamic Theming & Token Architecture
Theme changes between **Pastel** (Lavender, Mint, Sky, Porcelain) and **Dark** (Slate, Obsidian, Pastel Violet) are executed dynamically via `{DynamicResource}` lookups and atomic frozen `ResourceDictionary` swapping, eliminating window redraw flashes.
