/**
 * Simple PC Monitor - Product Landing Page Vanilla JS
 * Features: i18n Dictionary, Clean Language Switcher (No Flags), FAQ Accordion
 */

(function () {
  'use strict';

  // --- 1. Internationalization (i18n) Engine ---
  const translations = {
    es: {
      nav_features: "Características",
      nav_specs: "Especificaciones",
      nav_downloads: "Descargas",
      nav_faq: "FAQ",
      btn_download_nav: "Descargar v1.1.0",

      hero_badge: "Versión 1.1.0 • 585 KB Standalone",
      hero_title: "Monitor ultra ligero para Windows",
      hero_subtitle: "Telemetría en tiempo real, gestión de procesos, optimizador de memoria RAM y control de planes de energía en un único ejecutable sin dependencias.",
      btn_setup: "Descargar Instalador (.exe)",
      btn_standalone: "Ejecutable Standalone (585 KB)",
      btn_portable_zip: "Portable ZIP (1.9 MB)",
      hl_no_install: "Sin instalación requerida",
      hl_low_cpu: "0.0% CPU en reposo",
      hl_win_compat: "Windows 10 / 11 Nativo",
      hl_mit: "100% Código Abierto (MIT)",

      feat_tag: "Capacidades Nativas",
      feat_title: "Diseñado para máximo rendimiento sin sobrecarga",
      feat_desc: "Sin Electron, sin WebViews y sin frameworks pesados. C# y XAML puro compilado para Windows.",
      feat1_title: "Telemetría Win32 P/Invoke",
      feat1_desc: "Cálculos delta de alta precisión con GetSystemTimes y GlobalMemoryStatusEx con lectura en menos de 1 ms.",
      feat2_title: "Inspector 360° de Procesos",
      feat2_desc: "Nombres descriptivos reales, rutas de binarios y protección con lista negra para evitar cerrar procesos vitales del sistema operativo.",
      feat3_title: "Conmutador de Energía Win32",
      feat3_desc: "Cambio instantáneo de planes de energía (Equilibrado, Alto Rendimiento, Ahorro) en 0.01 ms sin requerir permisos de Administrador.",
      feat4_title: "Limpiador Temp & RAM Optimizer",
      feat4_desc: "Limpieza segura de archivos temporales antiguos (>24h) y vaciado de páginas en desuso con EmptyWorkingSet.",
      feat5_title: "Ping ICMP & Monitoreo de Discos",
      feat5_desc: "Medición constante de latencia de red y soporte para múltiples unidades y particiones de disco en tiempo real.",
      feat6_title: "4 Temas & 3 Modos de Ventana",
      feat6_desc: "Personaliza la interfaz con temas Pastel Dark, Pastel Light, Cyberpunk Neon y Sakura Rose, conmutables en 1 clic.",

      dl_tag: "Descarga Gratuita",
      dl_title: "Elige la edición ideal para tu equipo",
      dl_desc: "Todos los paquetes son 100% gratuitos, seguros y libres de software publicitario.",
      dl_card1_tag: "Recomendado",
      dl_card1_title: "Instalador Guiado",
      dl_card1_info: "Instalación estándar con accesos directos en el menú inicio y escritorio.",
      dl_card2_tag: "El más popular",
      dl_card2_title: "Standalone .EXE",
      dl_card2_info: "Un solo archivo ejecutable de 585 KB. Llévalo en una memoria USB y ejecútalo sin instalar.",
      dl_card3_tag: "Completo",
      dl_card3_title: "Portable ZIP",
      dl_card3_info: "Incluye todos los ensamblados y recursos de idioma (Español e Inglés) empaquetados.",
      btn_download: "Descargar Ahora",

      specs_title: "Ficha Técnica y Compatibilidad",
      spec_h_param: "Parámetro",
      spec_h_val: "Especificación",
      spec_row1_p: "Sistema Operativo",
      spec_row1_v: "Windows 10 (versión 1809 o superior) / Windows 11 (64-bit y 32-bit)",
      spec_row2_p: "Arquitectura",
      spec_row2_v: "C# / .NET Framework 4.8.1 (Nativo WPF XAML)",
      spec_row3_p: "Tamaño del Binario",
      spec_row3_v: "585 KB (Standalone Ejecutable único)",
      spec_row4_p: "Consumo de RAM",
      spec_row4_v: "~28 MB en segundo plano (optimizado)",
      spec_row5_p: "Permisos de Usuario",
      spec_row5_v: "Usuario estándar (No requiere elevación UAC para telemetría ni planes de energía)",
      spec_row6_p: "Licencia",
      spec_row6_v: "MIT License (Código abierto)",

      sec_title: "Garantía de Seguridad y Privacidad",
      sec_desc: "Simple PC Monitor no recopila datos, no se conecta a servidores externos para analíticas y no instala servicios en segundo plano. Código 100% auditable y transparente.",

      faq_tag: "Dudas Habituales",
      faq_title: "Preguntas Frecuentes",
      faq1_q: "¿Requiere privilegios de Administrador (UAC) para funcionar?",
      faq1_a: "No. Toda la telemetría de CPU, RAM, discos, red, conmutación de planes de energía y limpieza de archivos temporales de usuario opera con permisos normales de usuario estándar.",
      faq2_q: "¿Por qué el ejecutable es tan ligero (585 KB)?",
      faq2_a: "A diferencia de monitores modernos construidos con Electron que empaquetan un navegador Chromium entero (>150 MB), Simple PC Monitor está compilado directamente en C# nativo utilizando las librerías integradas de Windows.",
      faq3_q: "¿Cómo protege el sistema contra el cierre accidental de procesos?",
      faq3_a: "El gestor de procesos cuenta con una lista negra interna estricta que bloquea la finalización de componentes críticos como dwm.exe, csrss.exe, svchost.exe y explorer.exe.",
      faq4_q: "¿Dónde se guardan las configuraciones?",
      faq4_a: "Tus preferencias de tema, idioma y modo de visualización se guardan localmente en un archivo de configuración dentro de tu carpeta de usuario (%APPDATA%\\SimplePCMonitor).",

      footer_desc: "Monitor de recursos y suite de control de alto rendimiento para Windows.",
      footer_links_title: "Navegación",
      footer_repo_title: "Comunidad & Código",
      footer_copy: "Simple PC Monitor • Publicado bajo Licencia MIT."
    },
    en: {
      nav_features: "Features",
      nav_specs: "Specifications",
      nav_downloads: "Downloads",
      nav_faq: "FAQ",
      btn_download_nav: "Download v1.1.0",

      hero_badge: "Version 1.1.0 • 585 KB Standalone",
      hero_title: "Ultra-lightweight system monitor for Windows",
      hero_subtitle: "Real-time telemetry, process management, RAM optimizer, and native Win32 power plan control in a single standalone executable.",
      btn_setup: "Download Installer (.exe)",
      btn_standalone: "Standalone Binary (585 KB)",
      btn_portable_zip: "Portable ZIP (1.9 MB)",
      hl_no_install: "Zero Install Required",
      hl_low_cpu: "0.0% CPU Idle Overhead",
      hl_win_compat: "Native Windows 10 / 11",
      hl_mit: "100% Open Source (MIT)",

      feat_tag: "Native Capabilities",
      feat_title: "Engineered for pure speed without bloat",
      feat_desc: "No Electron, no WebViews, no heavy JavaScript runtimes. Pure compiled C# and XAML vector graphics.",
      feat1_title: "Win32 P/Invoke Telemetry",
      feat1_desc: "Sub-millisecond high precision delta math via GetSystemTimes and GlobalMemoryStatusEx.",
      feat2_title: "360° Process Inspector",
      feat2_desc: "Resolves real friendly application names and publishers with strict OS process protection blacklist.",
      feat3_title: "Win32 Power Plan Switcher",
      feat3_desc: "Instant 0.01 ms switching between Balanced, High Performance, and Power Saver schemes with PowrProf.dll.",
      feat4_title: "Safe Temp Cleaner & RAM Optimizer",
      feat4_desc: "Safely cleans %TEMP% files older than 24h and trims idle memory pages using EmptyWorkingSet.",
      feat5_title: "ICMP Ping & Multi-Volume Disks",
      feat5_desc: "Real-time network latency pinging and multi-partition disk space and throughput monitoring.",
      feat6_title: "4 Themes & 3 Viewport Modes",
      feat6_desc: "Personalize the interface with Pastel Dark, Pastel Light, Cyberpunk Neon, and Sakura Rose themes.",

      dl_tag: "Free Download",
      dl_title: "Choose the package that suits you best",
      dl_desc: "All release binaries are 100% free, standalone, and free of adware or third-party bundles.",
      dl_card1_tag: "Recommended",
      dl_card1_title: "Setup Installer",
      dl_card1_info: "Standard setup wizard with Start Menu and Desktop shortcuts.",
      dl_card2_tag: "Most Popular",
      dl_card2_title: "Standalone .EXE",
      dl_card2_info: "A single 585 KB executable file. Place it on a USB drive and run anywhere without installing.",
      dl_card3_tag: "Complete",
      dl_card3_title: "Portable ZIP",
      dl_card3_info: "Includes all localized satellite assemblies (English and Spanish) in a single zip archive.",
      btn_download: "Download Now",

      specs_title: "Technical Specifications & Compatibility",
      spec_h_param: "Parameter",
      spec_h_val: "Specification",
      spec_row1_p: "Operating System",
      spec_row1_v: "Windows 10 (version 1809+) / Windows 11 (64-bit & 32-bit)",
      spec_row2_p: "Architecture",
      spec_row2_v: "C# / .NET Framework 4.8.1 (Native WPF XAML)",
      spec_row3_p: "Binary Size",
      spec_row3_v: "585 KB (Single standalone .exe)",
      spec_row4_p: "Memory Footprint",
      spec_row4_v: "~28 MB background footprint (optimized)",
      spec_row5_p: "User Privileges",
      spec_row5_v: "Standard User (Zero UAC elevation needed for telemetry or power plans)",
      spec_row6_p: "License",
      spec_row6_v: "MIT License (Open Source)",

      sec_title: "Security & Privacy Guarantee",
      sec_desc: "Simple PC Monitor collects zero telemetry, makes zero unexpected background requests, and installs no background services. 100% auditable and transparent.",

      faq_tag: "Common Questions",
      faq_title: "Frequently Asked Questions",
      faq1_q: "Does it require Administrator (UAC) elevation?",
      faq1_a: "No. All CPU, RAM, disk, network latency metrics, power plan switching, and temporary file cleaning run under standard user permissions.",
      faq2_q: "Why is the executable so small (585 KB)?",
      faq2_a: "Unlike modern Electron-based tools that bundle an entire Chromium browser (>150 MB), Simple PC Monitor is compiled directly in native C# targeting built-in Windows runtime libraries.",
      faq3_q: "How does it prevent accidentally terminating system processes?",
      faq3_a: "The built-in Process Manager has a hardcoded protection blacklist preventing termination of critical OS tasks like dwm.exe, csrss.exe, svchost.exe, and explorer.exe.",
      faq4_q: "Where are configuration settings stored?",
      faq4_a: "Your theme, language, and window layout preferences are saved locally inside your user profile directory (%APPDATA%\\SimplePCMonitor).",

      footer_desc: "Ultra-fast telemetry dashboard and resource management suite for Windows.",
      footer_links_title: "Navigation",
      footer_repo_title: "Community & Code",
      footer_copy: "Simple PC Monitor • Released under the MIT License."
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
  }

  // --- 2. DOM Initialization ---
  document.addEventListener('DOMContentLoaded', () => {
    // Initial Setup
    setLanguage(currentLang);

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
