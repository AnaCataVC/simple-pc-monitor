# Technical Architecture: Zero-Dependency Visual Graphics in WPF & PowerShell

**Author:** ami-tech-lead  
**Created:** 2026-08-18  
**Project:** simple-pc-monitor  
**Status:** Validated  

---

## 1. Overview & Visual Enhancement Goals

To transform `simple-pc-monitor` into a highly visual, graphical dashboard without sacrificing zero-dependency purity or sub-millisecond execution speeds, we implement hardware-accelerated vector rendering using native WPF primitives:

1. **Real-Time Area Wave Charts (Pastel Waves):**
   - Renders a 30-sample sliding window into a `Polygon` with a translucent vertical `LinearGradientBrush` (fade to transparent) and a sharp `Polyline` stroke on top inside a responsive `Canvas`.
   - CPU and Network bandwidth histories update at 60 FPS without memory allocation thrashing.

2. **Circular Progress Rings & Radial Gauges:**
   - Evaluates arc segment geometries dynamically using trigonometric path calculations:
     $$x = cx + r \cdot \sin(\theta), \quad y = cy - r \cdot \cos(\theta)$$
   - Provides instant, striking visual percentage indicators for CPU, RAM, and primary Storage.

3. **In-Line Process Usage Bars:**
   - Enhances the process table by embedding color-coded proportional progress bars directly into each row template.

4. **Multi-Mode Window Layout (Dashboard vs. Mini HUD Widget):**
   - **Full Visual Analytics Mode:** Full dashboard with live wave charts, storage breakdown, hardware specs, and deep-dive process tabs.
   - **Compact HUD Widget Mode:** Minimalist, sleek desktop widget displaying 4 mini circular gauges and network rate with zero desktop clutter.
