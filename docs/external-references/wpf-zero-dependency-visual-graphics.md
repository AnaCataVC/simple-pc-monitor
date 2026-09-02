# Technical Architecture: Zero-Dependency Visual Graphics in C# WPF

**Author:** ami-tech-lead & ami-doc-architect  
**Updated:** 2026-09-02  
**Project:** simple-pc-monitor  
**Architecture:** C# .NET Framework 4.8 / WPF (Zero External Dependencies)  
**Status:** Validated  

---

## 1. Overview & Visual Rendering Architecture

To deliver a graphical, responsive telemetry dashboard without sacrificing zero-dependency purity (<600 KB standalone executable) or sub-millisecond dispatch cycles, `simple-pc-monitor` leverages native Windows Presentation Foundation (WPF) hardware-accelerated vector primitives:

1. **Real-Time Area Wave Charts (Dynamic Polygons):**
   - Renders a sliding window of historical samples into a WPF `Polygon` with a translucent vertical `LinearGradientBrush` (fade to transparent) and a sharp `Polyline` stroke inside a responsive `Canvas`.
   - CPU and Network throughput histories update smoothly without memory allocation thrashing, reusing geometric point collections.

2. **Circular Progress Rings & Radial Gauges:**
   - Calculates arc segment geometries dynamically using trigonometric path projections:
     $$x = cx + r \cdot \sin(\theta), \quad y = cy - r \cdot \cos(\theta)$$
   - Provides instant, low-latency visual percentage gauges for CPU, RAM, and primary Drive capacity.

3. **Bento Grid HUD & 4-Theme Dynamic Palette:**
   - Modular Bento HUD with responsive column definitions in `MainWindow.xaml`.
   - Runtime palette switching across 4 themes (**Pastel Dark**, **Pastel Light**, **Cyberpunk**, **Sakura**) by modifying application-level resource dictionaries without window reloading.

4. **Multi-Mode Window Layout (Dashboard vs. Mini HUD Widget):**
   - **Full Bento Dashboard:** Detailed telemetry with live wave charts, storage breakdown, AI agent process hierarchy, Windows services, and hardware specs.
   - **Compact HUD Widget Mode:** Minimalist desktop overlay displaying circular gauges and network rate with zero desktop clutter.

5. **In-Line Telemetry Progress Bars:**
   - Embedded color-coded horizontal bars directly in process data templates, providing at-a-glance visualization of CPU and RAM impact per process.
