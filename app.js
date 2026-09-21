/**
 * System Core Monitor - Product Landing Page Vanilla JS
 * Features: i18n Dictionary, Clean Language Switcher (No Flags), FAQ Accordion, Dynamic GitHub Releases Resolver
 */

(function () {
  'use strict';

  let latestVersionTag = 'v3.2.1';
  const repoOwner = 'AnaCataVC';
  const repoName = 'system-core-monitor';

  // Strings bilingües de la interfaz web
  const translations = {
    es: {
      nav_overview: "Resumen",
      nav_features: "Módulos",
      nav_architecture: "Arquitectura",
      nav_install: "Instalación",
      nav_actions: "Acciones",
      nav_specs: "Especificaciones",
      nav_downloads: "Descargas",
      nav_faq: "FAQ",
      btn_download_nav: "Descargar v3.2.1",

      hero_pill: "Windows 10 / 11 Nativo • .NET 9 C# 13",
      hero_badge: "Versión 3.2.1 • 687 KB Standalone • .NET 9",
      hero_title: "Monitor de Rendimiento y Agentes IA para Windows",
      hero_subtitle: "Telemetría en tiempo real, observabilidad de Claude Code y Gemini, mantenimiento de sesiones y optimización de recursos en un único ejecutable nativo.",
      btn_setup: "Descargar Instalador (.exe)",
      btn_standalone: "Ejecutable Standalone (617 KB)",
      btn_portable_zip: "Portable ZIP (620 KB)",
      hl_no_install: "Sin instalación requerida",
      hl_low_cpu: "0.0% CPU en reposo",
      hl_win_compat: "Windows 10 / 11 Nativo",
      hl_mit: "100% Código Abierto (MIT)",

      feat_tag: "Capacidades Nativas v3.0",
      feat_title: "Diseñado para máximo rendimiento y observabilidad de IA",
      feat_desc: "Sin Electron, sin WebViews y sin dependencias externas. Compilado en C# 13 y .NET 9 con arquitectura modular MVVM.",
      feat1_title: "Telemetría Win32 P/Invoke",
      feat1_desc: "Cálculos delta de alta precisión con GetSystemTimes y GlobalMemoryStatusEx estructurado por referencia sin presión sobre el Garbage Collector.",
      feat2_title: "Agentes IA & Servidores MCP",
      feat2_desc: "Observabilidad en tiempo real de Claude Code CLI, Gemini y servidores MCP compilados (Go, Rust, Node, Python) con telemetría de CPU y RAM dedicada.",
      feat3_title: "Mantenimiento de Transcripts IA",
      feat3_desc: "Escaneo y depuración inteligente de historiales .jsonl (>7d) con ventana de gracia inviolable de 24h y protección de archivos de configuración.",
      feat4_title: "Conmutador de Energía Win32",
      feat4_desc: "Cambio instantáneo de planes de energía (Equilibrado, Alto Rendimiento, Ahorro) en 0.01 ms con PowrProf.dll sin requerir permisos de Administrador.",
      feat5_title: "Limpiador Temp & RAM Optimizer",
      feat5_desc: "Limpieza multizona de archivos temporales antiguos (>24h), vaciado de páginas en desuso con EmptyWorkingSet y podado de cachés de compilación.",
      feat6_title: "Aceleradores GPU & NPU",
      feat6_desc: "Detección de hardware gráfico DirectX DXGI y coprocesadores neuronales NPU de IA mediante llamadas nativas SetupAPI.",
      feat7_title: "Inspector 360° y Guarda PID",
      feat7_desc: "Cierre elegante en dos fases (WM_CLOSE), terminación de árboles en orden inverso y protección contra reciclaje de PID de Windows.",
      feat8_title: "Ergonomía Visual Bento",
      feat8_desc: "Tipografía ampliada a 14-16px, iconos vectoriales nítidos de 20x20, panel de navegación de 240px y 4 temas dinámicos en XAML.",

      act_tag: "Centro de Mando Activo",
      act_title: "Guía de Botones de Acción y Control Nativo",
      act_desc: "Pasa de la observación pasiva al control directo. Cada botón ejecuta llamadas nativas directas al kernel y subsistemas de Windows.",
      act1_title: "🚀 Modo Turbo",
      act1_desc: "Conmuta el plan de energía a Alto Rendimiento (desestaciona núcleos de CPU) y purga agresivamente la memoria RAM física en procesos de usuario.",
      act2_title: "🌐 Vaciar DNS",
      act2_desc: "Purga instantáneamente la caché de nombres DNS del sistema operativo en 0.01 ms para solucionar errores de red y webs no disponibles.",
      act3_title: "🧹 Limpiar Temporales",
      act3_desc: "Purga archivos residuales en %TEMP%, Windows\\Temp y WinSxS (>24h). Blindado contra escape de Junctions NTFS y guarda de doble timestamp.",
      act4_title: "🗄️ Transcripts IA",
      act4_desc: "Escanea y libera cientos de megabytes de historiales inactivos de Claude Code y Gemini conservando íntegras tus sesiones de trabajo activas.",
      act5_title: "⏸️ Suspender y Reanudar",
      act5_desc: "Congela procesos desbocados bajando su consumo a 0% CPU sin cerrarlos ni perder información, con reactivación instantánea.",
      act6_title: "⚡ Rescate de Procesos",
      act6_desc: "Detección de aplicaciones colgadas (IsResponding == false) con solicitud de cierre elegante y terminación segura de subprocesos huérfanos.",

      dl_tag: "Descarga Gratuita",
      dl_title: "Elige la edición ideal para tu equipo",
      dl_desc: "Todos los paquetes son 100% gratuitos, seguros y libres de software publicitario.",
      dl_card1_tag: "Recomendado",
      dl_card1_title: "Instalador Guiado",
      dl_card1_info: "Instalación estándar con accesos directos en el menú inicio y escritorio.",
      dl_card2_tag: "El más popular",
      dl_card2_title: "Standalone .EXE",
      dl_card2_info: "Un solo archivo ejecutable de 617 KB. Llévalo en una memoria USB y ejecútalo sin instalar.",
      dl_card3_tag: "Completo",
      dl_card3_title: "Portable ZIP",
      dl_card3_info: "Incluye el ejecutable autónomo, documentación y configuración portable empaquetados.",
      btn_download: "Descargar Ahora",

      specs_title: "Ficha Técnica y Compatibilidad",
      spec_h_param: "Parámetro",
      spec_h_val: "Especificación",
      spec_row1_p: "Sistema Operativo",
      spec_row1_v: "Windows 10 (versión 1809 o superior) / Windows 11 (64-bit)",
      spec_row2_p: "Arquitectura & Runtime",
      spec_row2_v: "C# 13 / .NET 9 (Nativo WPF XAML, arquitectura modular MVVM)",
      spec_row3_p: "Tamaño del Binario",
      spec_row3_v: "617 KB (Standalone Ejecutable único, 0 dependencias NuGet)",
      spec_row4_p: "Consumo de RAM",
      spec_row4_v: "~35 MB en segundo plano (optimizado)",
      spec_row5_p: "Permisos de Usuario",
      spec_row5_v: "Usuario estándar (No requiere elevación UAC para telemetría ni planes de energía)",
      spec_row6_p: "Licencia",
      spec_row6_v: "MIT License (Código abierto)",

      sec_title: "Garantía de Seguridad y Privacidad",
      sec_desc: "System Core Monitor no recopila datos personales, no envía telemetría externa a servidores remotos y no instala servicios ocultos. Código 100% auditable y transparente.",

      faq_tag: "Dudas Habituales",
      faq_title: "Preguntas Frecuentes",
      faq1_q: "¿Requiere privilegios de Administrador (UAC) para funcionar?",
      faq1_a: "No. Toda la telemetría de CPU, RAM, discos, red, conmutación de planes de energía, agentes IA y limpieza de archivos temporales opera con permisos normales de usuario estándar.",
      faq2_q: "¿Por qué el ejecutable es tan ligero (617 KB)?",
      faq2_a: "A diferencia de monitores construidos con Electron que empaquetan Chromium y Node (>150 MB), System Core Monitor está compilado en C# nativo sobre .NET 9 con cero dependencias externas de terceros.",
      faq3_q: "¿Cómo detecta los agentes IA y servidores MCP?",
      faq3_a: "Examina los árboles de procesos nativos de Toolhelp32 identificando ejecutables de Claude CLI, Gemini y servidores MCP (Go, Rust, Node, Python), calculando su consumo de recursos agregado.",
      faq4_q: "¿Es seguro el limpiador de transcripts IA?",
      faq4_a: "Sí. Aplica una ventana de gracia inviolable de 24 horas para nunca tocar sesiones recientes, verifica que el PID no esté activo y protege listas negras de configuración (CLAUDE.md, GEMINI.md, settings.json).",

      footer_desc: "Monitor de recursos y suite de observabilidad de agentes IA de alto rendimiento para Windows.",
      footer_links_title: "Navegación",
      footer_repo_title: "Comunidad & Código",
      footer_copy: "System Core Monitor • Publicado bajo Licencia MIT."
    },
    en: {
      nav_features: "Features",
      nav_actions: "Actions",
      nav_specs: "Specifications",
      nav_downloads: "Downloads",
      nav_faq: "FAQ",
      btn_download_nav: "Download v3.2.1",

      hero_badge: "Version 3.2.1 • 687 KB Standalone • .NET 9",
      hero_title: "High-Performance System & AI Agent Monitor for Windows",
      hero_subtitle: "Real-time telemetry, Claude Code & Gemini observability, transcript storage maintenance, and resource optimization in a single standalone binary.",
      btn_setup: "Download Installer (.exe)",
      btn_standalone: "Standalone Binary (617 KB)",
      btn_portable_zip: "Portable ZIP (620 KB)",
      hl_no_install: "Zero Install Required",
      hl_low_cpu: "0.0% CPU Idle Overhead",
      hl_win_compat: "Native Windows 10 / 11",
      hl_mit: "100% Open Source (MIT)",

      feat_tag: "Native Capabilities v3.0",
      feat_title: "Engineered for pure speed and AI agent observability",
      feat_desc: "No Electron, no WebViews, zero external dependencies. Pure compiled C# 13 and .NET 9 with modular MVVM architecture.",
      feat1_title: "Win32 P/Invoke Telemetry",
      feat1_desc: "Sub-millisecond high-precision delta math via GetSystemTimes and GlobalMemoryStatusEx passed by reference with zero GC pressure.",
      feat2_title: "AI Agents & MCP Servers",
      feat2_desc: "Real-time observability of Claude Code CLI, Gemini, and compiled MCP subprocesses (Go, Rust, Node, Python) with dedicated CPU and RAM tracking.",
      feat3_title: "AI Transcript Retention & Cleanup",
      feat3_desc: "Intelligent scanning and safe pruning of stale session .jsonl files (>7d) with an inviolable 24-hour grace window and config file protection.",
      feat4_title: "Win32 Power Plan Switcher",
      feat4_desc: "Instant 0.01 ms switching between Balanced, High Performance, and Power Saver schemes with PowrProf.dll without UAC elevation.",
      feat5_title: "Safe Temp Cleaner & RAM Optimizer",
      feat5_desc: "Multi-zone cleaning of temporary files (>24h), idle memory page trimming with EmptyWorkingSet, and regenerable build cache cleanup.",
      feat6_title: "GPU & NPU Hardware Accelerators",
      feat6_desc: "DirectX DXGI graphics hardware discovery and neural processing unit (NPU) accelerator probing via native SetupAPI calls.",
      feat7_title: "360° Inspector & PID Reuse Guard",
      feat7_desc: "Two-phase graceful close (WM_CLOSE), reverse topological tree termination, and Windows PID recycling safeguards.",
      feat8_title: "Bento Visual Ergonomics",
      feat8_desc: "Enhanced 14-16px typography, sharp 20x20 vector icons, 240px navigation sidebar, and 4 dynamic XAML themes.",

      act_tag: "Active Command Center",
      act_title: "Action Buttons & Native Control Matrix",
      act_desc: "Transition from passive telemetry to direct OS command. Every button triggers direct Win32/kernel API calls.",
      act1_title: "🚀 Turbo Mode",
      act1_desc: "Instantly switches to High Performance power plan (unparking CPU cores) and aggressively purges idle RAM working sets.",
      act2_title: "🌐 Flush DNS",
      act2_desc: "Directly resets the Windows DNS resolver cache in 0.01 ms to resolve networking glitches and unreachable web pages.",
      act3_title: "🧹 Clean Temp Storage",
      act3_desc: "Safely clears cache files in %TEMP%, Windows\\Temp, and WinSxS (>24h). Isolated against NTFS Junctions with dual-timestamp safety gate.",
      act4_title: "🗄️ AI Transcripts",
      act4_desc: "Scans and safely reclaims hundreds of megabytes of stale Claude Code and Gemini session histories while protecting active sessions.",
      act5_title: "⏸️ Suspend & Resume",
      act5_desc: "Freezes runaway background tasks dropping CPU to 0% without closing windows, with instant resume capability.",
      act6_title: "⚡ Process Rescue",
      act6_desc: "Detects unresponsive applications (IsResponding == false) with graceful close requests and safe termination of orphan subprocesses.",

      dl_tag: "Free Download",
      dl_title: "Choose the package that suits you best",
      dl_desc: "All release binaries are 100% free, standalone, and free of adware or third-party bundles.",
      dl_card1_tag: "Recommended",
      dl_card1_title: "Setup Installer",
      dl_card1_info: "Standard setup wizard with Start Menu and Desktop shortcuts.",
      dl_card2_tag: "Most Popular",
      dl_card2_title: "Standalone .EXE",
      dl_card2_info: "A single 617 KB executable file. Place it on a USB drive and run anywhere without installing.",
      dl_card3_tag: "Complete",
      dl_card3_title: "Portable ZIP",
      dl_card3_info: "Includes the standalone executable, documentation, and portable settings in a single archive.",
      btn_download: "Download Now",

      specs_title: "Technical Specifications & Compatibility",
      spec_h_param: "Parameter",
      spec_h_val: "Specification",
      spec_row1_p: "Operating System",
      spec_row1_v: "Windows 10 (version 1809+) / Windows 11 (64-bit)",
      spec_row2_p: "Architecture & Runtime",
      spec_row2_v: "C# 13 / .NET 9 (Native WPF XAML, modular MVVM architecture)",
      spec_row3_p: "Binary Size",
      spec_row3_v: "617 KB (Single standalone .exe, 0 NuGet dependencies)",
      spec_row4_p: "Memory Footprint",
      spec_row4_v: "~35 MB background footprint (optimized)",
      spec_row5_p: "User Privileges",
      spec_row5_v: "Standard User (Zero UAC elevation needed for telemetry or power plans)",
      spec_row6_p: "License",
      spec_row6_v: "MIT License (Open Source)",

      sec_title: "Security & Privacy Guarantee",
      sec_desc: "System Core Monitor collects zero telemetry, makes zero unexpected background requests, and installs no background services. 100% auditable and transparent.",

      faq_tag: "Common Questions",
      faq_title: "Frequently Asked Questions",
      faq1_q: "Does it require Administrator (UAC) elevation?",
      faq1_a: "No. All CPU, RAM, disk, network latency metrics, power plan switching, AI agent monitoring, and temp cleaning run under standard user permissions.",
      faq2_q: "Why is the executable so small (617 KB)?",
      faq2_a: "Unlike modern Electron-based tools that bundle an entire Chromium browser and Node (>150 MB), System Core Monitor is compiled directly in native C# targeting .NET 9 with zero third-party dependencies.",
      faq3_q: "How does it monitor AI agents and MCP servers?",
      faq3_a: "It leverages Win32 Toolhelp32 process snapshots to discover Claude Code CLI, Gemini, and MCP server child processes (Go, Rust, Node, Python), aggregating their resource consumption.",
      faq4_q: "Is the AI transcript cleaner safe to run?",
      faq4_a: "Yes. It strictly enforces a 24-hour inviolable grace window for recent files, checks live process PIDs to avoid active sessions, and blacklists config and memory files (CLAUDE.md, GEMINI.md, settings.json).",

      footer_desc: "High-performance telemetry dashboard and AI agent observability suite for Windows.",
      footer_links_title: "Navigation",
      footer_repo_title: "Community & Code",
      footer_copy: "System Core Monitor • Released under the MIT License."
    }
  };

  let currentLang = localStorage.getItem('spm_lang') || 'es';

  function setLanguage(lang) {
    if (!translations[lang]) return;
    currentLang = lang;
    localStorage.setItem('spm_lang', lang);

    document.querySelectorAll('[data-i18n]').forEach(el => {
      const key = el.getAttribute('data-i18n');
      if (translations[lang][key]) {
        el.textContent = translations[lang][key];
      }
    });

    const langBtn = document.getElementById('lang-toggle-btn');
    if (langBtn) {
      langBtn.textContent = lang === 'es' ? 'EN' : 'ES';
    }

    const navBtn = document.getElementById('nav-dl-btn');
    if (navBtn) {
      navBtn.textContent = lang === 'es' ? `Descargar ${latestVersionTag}` : `Download ${latestVersionTag}`;
    }
  }

  // --- 2. Dynamic GitHub Release Fetcher ---
  async function fetchLatestRelease() {
    try {
      const response = await fetch('https://api.github.com/repos/AnaCataVC/system-core-monitor/releases/latest');
      if (!response.ok) return;
      const release = await response.json();
      if (!release || !release.tag_name) return;

      latestVersionTag = release.tag_name;

      // Update brand and nav badges
      const brandBadge = document.getElementById('brand-badge');
      if (brandBadge) brandBadge.textContent = latestVersionTag;

      const navBtn = document.getElementById('nav-dl-btn');
      if (navBtn) {
        navBtn.textContent = currentLang === 'es' ? `Descargar ${latestVersionTag}` : `Download ${latestVersionTag}`;
      }

      // Match release assets
      let setupUrl = '';
      let standaloneUrl = '';
      let portableZipUrl = '';

      if (Array.isArray(release.assets)) {
        for (const asset of release.assets) {
          const name = (asset.name || '').toLowerCase();
          if (name.endsWith('-setup.exe') || name === 'systemcoremonitor-setup.exe' || name === 'simplepcmonitor-setup.exe') {
            setupUrl = asset.browser_download_url;
          } else if (name === 'systemcoremonitor.exe' || name === 'simplepcmonitor.exe') {
            standaloneUrl = asset.browser_download_url;
          } else if (name.includes('portable') && name.endsWith('.zip')) {
            portableZipUrl = asset.browser_download_url;
          }
        }
      }

      // Update download buttons
      if (setupUrl) {
        document.querySelectorAll('[data-dl-type="setup"]').forEach(el => { el.href = setupUrl; });
      }
      if (standaloneUrl) {
        document.querySelectorAll('[data-dl-type="standalone"]').forEach(el => { el.href = standaloneUrl; });
      }
      if (portableZipUrl) {
        document.querySelectorAll('[data-dl-type="portable"]').forEach(el => { el.href = portableZipUrl; });
      }
    } catch (e) {
      // Graceful fallback to static URLs in index.html
    }
  }

  // --- 3. DOM Initialization ---
  document.addEventListener('DOMContentLoaded', () => {
    // Initial Setup
    setLanguage(currentLang);
    fetchLatestRelease();

    // Clean Language Toggle Button Handler (No Flags)
    const langBtn = document.getElementById('lang-toggle-btn');
    if (langBtn) {
      langBtn.addEventListener('click', () => {
        setLanguage(currentLang === 'es' ? 'en' : 'es');
      });
    }

    // FAQ Accordion Handler
    document.querySelectorAll('.faq-question').forEach(btn => {
      btn.addEventListener('click', () => {
        const item = btn.closest('.faq-item');
        const isActive = item.classList.contains('active');
        document.querySelectorAll('.faq-item').forEach(i => i.classList.remove('active'));
        if (!isActive) {
          item.classList.add('active');
        }
      });
    });
  });
})();
