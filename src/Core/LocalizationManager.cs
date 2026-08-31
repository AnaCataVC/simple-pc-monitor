using System;
using System.Collections.Generic;

namespace SimplePCMonitor.Core
{
    public static class LocalizationManager
    {
        private static string _currentLanguage = "es";
        public static string CurrentLanguage
        {
            get { return _currentLanguage; }
            set { _currentLanguage = value; }
        }

        private static readonly Dictionary<string, string> StringsEs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // App Title & Header
            { "AppTitle", "Simple PC Monitor" },
            { "UptimeTooltip", "Tiempo continuo que la computadora lleva encendida desde el último reinicio" },
            { "UptimeLabel", "Activo" },
            { "CoresBadge", "{0} Núcleos" },
            { "PinAlwaysOnTop", "Fijar Siempre Visible (Always on Top)" },
            { "Unpin", "Desanclar Ventana" },
            { "Minimize", "Minimizar" },
            { "Maximize", "Maximizar" },
            { "Restore", "Restaurar" },
            { "Close", "Cerrar" },

            // Ribbon Actions
            { "TrimRam", "Optimizar RAM" },
            { "TrimRamTooltip", "Reducir conjunto de trabajo y liberar memoria RAM" },
            { "CleanTemp", "Limpiar Temp" },
            { "CleanDeep", "Limpiar Temp" },
            { "CleanTempTooltip", "Limpieza profunda y segura de temporales, Windows Update y caché de navegadores" },
            { "CleanDeepTooltip", "Limpieza profunda y segura de temporales, Windows Update y caché de navegadores" },
            { "TurboMode", "Modo Turbo" },
            { "TurboModeTooltip", "1-Clic: Activa Máximo Rendimiento + Purga de Memoria RAM" },
            { "FlushDns", "Vaciar DNS" },
            { "FlushDnsTooltip", "Limpiar la caché del solucionador DNS de Windows" },
            { "Snapshot", "Captura" },
            { "SnapshotTooltip", "Copiar informe de diagnóstico en Markdown al portapapeles" },

            // Power Plans
            { "PowerPlanSaver", "Eco" },
            { "PowerPlanBalanced", "Equilibrado" },
            { "PowerPlanHighPerf", "Turbo" },
            { "PowerPlanSaverTooltip", "Cambiar al plan de ahorro de energía" },
            { "PowerPlanBalancedTooltip", "Cambiar al plan equilibrado de Windows" },
            { "PowerPlanHighPerfTooltip", "Cambiar al plan de alto rendimiento" },

            // Tools Dropdown
            { "Tools", "Herramientas" },
            { "ToolsTooltip", "Herramientas de diagnóstico del sistema Windows" },
            { "ToolTaskMgr", "📊 Administrador de Tareas" },
            { "ToolResMon", "📈 Monitor de Recursos (Resmon)" },
            { "ToolStorageSense", "🛡️ Sensor de Almacenamiento" },
            { "ToolServices", "⚙️ Gestión de Servicios de Windows" },

            // View Modes Dropdown
            { "ViewFull", "Completo" },
            { "ViewHero", "Modo Compacto" },
            { "ViewWidget", "Modo Mini Widget" },
            { "ViewModeTooltip", "Cambiar Modo de Vista (Completo / Compacto / Mini Widget)" },
            { "MenuFullDesc", "🖥️ Modo Completo (Dashboard + Procesos)" },
            { "MenuHeroDesc", "📊 Modo Compacto (Tarjetas y Ondas)" },
            { "MenuWidgetDesc", "📌 Modo Mini Widget (Barra Flotante)" },

            // Themes Dropdown
            { "ThemeTooltip", "Cambiar Tema Visual (Dark, Light, Neon, Rose)" },
            { "ThemeDark", "🌙 Pastel Dark (Oscuro)" },
            { "ThemeLight", "☀️ Pastel Light (Claro)" },
            { "ThemeNeon", "⚡ Pastel Neon (Cyberpunk)" },
            { "ThemeRose", "🌸 Pastel Rose (Rosa Pastel)" },

            // Language
            { "LangTooltip", "Cambiar Idioma / Change Language" },

            // Interval
            { "IntervalTooltip", "Cambiar Intervalo de Refresco (3s / 5s)" },

            // Bento Cards
            { "CardCpuTitle", "PROCESADOR" },
            { "CardGpuTitle", "GRÁFICOS" },
            { "CardNpuTitle", "NPU (IA)" },
            { "CardRamTitle", "MEMORIA RAM" },
            { "CardDiskTitle", "ALMACENAMIENTO" },
            { "CardNetTitle", "RED" },

            { "GpuDiscreteBadge", "GPU Dedicada" },
            { "GpuIntegratedBadge", "Gráficos Integrados" },
            { "GpuDirect3DBadge", "Motor 3D" },
            { "NpuIdleBadge", "Inactivo" },
            { "NpuActiveBadge", "En Uso" },
            { "NpuNotDetected", "No Detectado" },
            { "RamUsedLabel", "{0:N1} / {1:N1} GB Usado" },
            { "DiskFreeLabel", "{0:N0} GB Libres" },

            // Live Wave Charts
            { "WaveCpuTitle", "CARGA DE CPU (ONDA 30s EN VIVO)" },
            { "WaveNetTitle", "TRÁFICO DE RED (ONDA 30s EN VIVO)" },
            { "PeakLabel", "Pico" },

            // Deep Dive Tabs
            { "TabProcesses", "📊 Procesos" },
            { "TabAccelerators", "⚡ Aceleradores (GPU/NPU)" },
            { "TabServices", "⚙️ Servicios" },
            { "TabTasks", "⏱️ Tareas Programadas" },
            { "TabStartup", "🚀 Apps de Inicio" },
            { "TabDrives", "💾 Discos & Almacenamiento" },

            { "TabProcessesSummary", "Principales Procesos en Ejecución" },
            { "TabAcceleratorsSummary", "Diagnóstico de Aceleradores GPU y NPU" },
            { "TabServicesSummary", "Estado de Servicios del Sistema" },
            { "TabTasksSummary", "Tareas Programadas de Windows" },
            { "TabStartupSummary", "Aplicaciones de Inicio Automático" },
            { "TabDrivesSummary", "Unidades de Disco y Particiones" },

            // Process Controls & Filters
            { "SearchPlaceholder", "🔍 Buscar proceso por nombre o PID..." },
            { "SortByCpu", "⚡ Orden: CPU %" },
            { "SortByRam", "🧠 Orden: RAM MB" },
            { "UnresponsiveWarning", "⚠️ {0} Proceso(s) colgado(s)" },
            { "RescueUnresponsive", "Rescatar / Cerrar" },

            // Table & Column Headers
            { "ColPid", "PID" },
            { "ColApp", "APLICACIÓN" },
            { "ColCpu", "CPU %" },
            { "ColWorkingSet", "MEMORIA" },
            { "ColRamPercent", "% RAM" },
            { "ColState", "ESTADO" },
            { "ColPriority", "PRIORIDAD" },
            { "ColActions", "ACCIONES" },
            { "ColServiceName", "SERVICIO DE WINDOWS" },
            { "ColServiceStatus", "ESTADO" },
            { "ColTaskName", "TAREA PROGRAMADA" },
            { "ColTaskState", "ESTADO" },
            { "ColStartupApp", "APLICACIÓN / PROGRAMA" },
            { "ColStartupLocation", "ORIGEN" },
            { "ColStartupStatus", "ESTADO" },
            { "ColStartupActions", "ACCIONES" },

            // Hardware & Storage Deck Headers
            { "CardHardwareTitle", "SISTEMA Y HARDWARE" },
            { "HwProcessor", "PROCESADOR" },
            { "HwGraphics", "ADAPTADOR GRÁFICO" },
            { "HwNpu", "MOTOR IA (NPU)" },
            { "HwOsLabel", "SISTEMA OPERATIVO" },
            { "HwPowerSchemeLabel", "PLAN DE ENERGÍA" },
            { "StorageVolumes", "UNIDADES DE ALMACENAMIENTO" },

            // Accelerators Tab
            { "GpuDeckTitle", "PROCESADOR GRÁFICO (GPU / VRAM)" },
            { "Gpu3DRendering", "RENDERIZADO 3D" },
            { "GpuComputeML", "CÓMPUTO / IA" },
            { "GpuVideoDecode", "DECODIFICACIÓN VIDEO" },
            { "GpuCopyEngine", "MOTOR DE COPIA" },
            { "GpuVramTitle", "MEMORIA DE VIDEO DEDICADA (VRAM)" },

            { "NpuDeckTitle", "UNIDAD DE PROCESAMIENTO NEURAL (NPU / MOTOR IA)" },
            { "NpuDeckDesc", "Acelerador de bajo consumo integrado en el SoC para efectos de cámara de Windows Studio, DirectML e inferencia local de IA." },
            { "NpuComputeUtilization", "UTILIZACIÓN DEL MOTOR DE CÓMPUTO NEURAL" },

            // Context Menus & Action Tooltips
            { "MenuDetails", "Ver Detalles del Proceso" },
            { "MenuSuspend", "⏸ Pausar Proceso (Suspend)" },
            { "MenuResume", "▶ Reanudar Proceso (Resume)" },
            { "MenuPriority", "⚡ Prioridad de CPU" },
            { "MenuOpenFolder", "Abrir Carpeta del Archivo" },
            { "MenuSearchOnline", "Buscar en Google" },
            { "MenuEndProcess", "Finalizar Proceso" },
            { "MenuOpenDrive", "Abrir Unidad en Explorador" },
            { "MenuCleanDrive", "Limpiar Unidad (Storage Sense)" },
            { "MenuServiceStart", "▶️ Iniciar Servicio" },
            { "MenuServiceStop", "⏹️ Detener Servicio" },
            { "MenuServiceRestart", "🔄 Reiniciar Servicio" },
            { "MenuOpenServicesMsc", "⚙️ Abrir Consola de Servicios (services.msc)" },
            { "MenuTaskRun", "▶️ Ejecutar Tarea Ahora" },
            { "MenuTaskEnd", "⏹️ Detener Tarea" },
            { "MenuOpenTaskSchd", "📅 Abrir Programador de Tareas (taskschd.msc)" },
            { "MenuStartupOpenFolder", "📂 Abrir Ubicación del Archivo" },
            { "MenuStartupSearch", "🔍 Buscar Programa en Google" },
            { "MenuStartupCopyCmd", "📋 Copiar Comando / Ruta" },
            { "MenuStartupSettings", "⚙️ Abrir Configuración de Inicio de Windows" },
            { "TooltipOpenFolder", "Abrir Carpeta" },
            { "TooltipSearchOnline", "Buscar Online" },
            { "TooltipOpenNativeTools", "Herramientas del Sistema" },

            // Mini Widget
            { "WidgetRestore", "🖥️ Restaurar a Modo Completo" },
            { "WidgetSwitchHero", "📊 Cambiar a Modo Compacto" },
            { "WidgetSnapBottomRight", "📍 Acoplar en Esquina Inferior Derecha" },
            { "WidgetPinAlways", "📌 Fijar Siempre Visible (Always on Top)" },
            { "WidgetClose", "❌ Cerrar Monitor" },
            { "WidgetTooltip", "Doble clic para volver al Modo Completo (o clic derecho para opciones)" },

            // Toast Messages
            { "ToastRamTrimmed", "⚡ Memoria RAM optimizada ({0} procesos liberados)" },
            { "ToastTempCleaned", "🧹 Se liberaron {0} en Limpieza Profunda ({1} archivos)" },
            { "ToastTurboMode", "🚀 ¡Modo Turbo Activado! (Alto Rendimiento + Purga RAM)" },
            { "ToastDnsFlushed", "🌐 ¡Caché DNS vaciada con éxito!" },
            { "ToastSnapshotCopied", "📸 ¡Informe de diagnóstico copiado al portapapeles!" },
            { "ToastPowerPlan", "⚡ Plan de energía: {0}" },
            { "ToastTheme", "🎨 Tema: {0}" },
            { "ToastInterval", "⏱️ Intervalo: {0}s" },
            { "ToastPinned", "📌 Ventana fijada al frente" },
            { "ToastUnpinned", "📌 Ventana desanclada" },
            { "ToastWidgetDocked", "📍 Widget acoplado a la esquina inferior derecha" },
            { "ToastProcessEnded", "🔴 Proceso finalizado: {0}" },
            { "ToastProcessSuspended", "⏸ Proceso suspendido: {0}" },
            { "ToastProcessResumed", "▶ Proceso reanudado: {0}" },
            { "ToastPriorityChanged", "⚡ Prioridad de {0} cambiada a {1}" },
            { "ToastServiceStarted", "▶️ Servicio iniciado: {0}" },
            { "ToastServiceStopped", "⏹️ Servicio detenido: {0}" },
            { "ToastTaskExecuted", "📅 Tarea ejecutada: {0}" },
            { "ToastStartupCopied", "📋 Ruta copiada al portapapeles" }
        };

        private static readonly Dictionary<string, string> StringsEn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // App Title & Header
            { "AppTitle", "Simple PC Monitor" },
            { "UptimeTooltip", "Continuous time the computer has been running since the last reboot" },
            { "UptimeLabel", "Uptime" },
            { "CoresBadge", "{0} Cores" },
            { "PinAlwaysOnTop", "Pin Always on Top" },
            { "Unpin", "Unpin Window" },
            { "Minimize", "Minimize" },
            { "Maximize", "Maximize" },
            { "Restore", "Restore" },
            { "Close", "Close" },

            // Ribbon Actions
            { "TrimRam", "Trim RAM" },
            { "TrimRamTooltip", "Trim process working sets and optimize RAM" },
            { "CleanTemp", "Clean Temp" },
            { "CleanDeep", "Clean Temp" },
            { "CleanTempTooltip", "Safely clean temporary files, Windows Update downloads, and browser caches" },
            { "CleanDeepTooltip", "Safely clean temporary files, Windows Update downloads, and browser caches" },
            { "TurboMode", "Turbo Mode" },
            { "TurboModeTooltip", "1-Click: Activate High Performance + Purge RAM" },
            { "FlushDns", "Flush DNS" },
            { "FlushDnsTooltip", "Flush Windows DNS resolver cache" },
            { "Snapshot", "Snapshot" },
            { "SnapshotTooltip", "Copy Markdown diagnostic snapshot to clipboard" },

            // Power Plans
            { "PowerPlanSaver", "Eco" },
            { "PowerPlanBalanced", "Balanced" },
            { "PowerPlanHighPerf", "Turbo" },
            { "PowerPlanSaverTooltip", "Switch to Power Saver plan" },
            { "PowerPlanBalancedTooltip", "Switch to Windows Balanced plan" },
            { "PowerPlanHighPerfTooltip", "Switch to High Performance plan" },

            // Tools Dropdown
            { "Tools", "Tools" },
            { "ToolsTooltip", "Windows diagnostic tools" },
            { "ToolTaskMgr", "📊 Windows Task Manager" },
            { "ToolResMon", "📈 Resource Monitor (Resmon)" },
            { "ToolStorageSense", "🛡️ Storage Sense / PC Manager" },
            { "ToolServices", "⚙️ Windows Services Management" },

            // View Modes Dropdown
            { "ViewFull", "Full" },
            { "ViewHero", "Compact" },
            { "ViewWidget", "Mini Widget" },
            { "ViewModeTooltip", "Switch View Mode (Full / Compact / Mini Widget)" },
            { "MenuFullDesc", "🖥️ Full Mode (Dashboard + Processes)" },
            { "MenuHeroDesc", "📊 Compact Mode (Cards & Waves)" },
            { "MenuWidgetDesc", "📌 Mini Widget (Floating Bar)" },

            // Themes Dropdown
            { "ThemeTooltip", "Switch Visual Theme (Dark, Light, Neon, Rose)" },
            { "ThemeDark", "🌙 Pastel Dark" },
            { "ThemeLight", "☀️ Pastel Light" },
            { "ThemeNeon", "⚡ Pastel Neon" },
            { "ThemeRose", "🌸 Pastel Rose" },

            // Language
            { "LangTooltip", "Change Language / Cambiar Idioma" },

            // Interval
            { "IntervalTooltip", "Switch Refresh Interval (3s / 5s)" },

            // Bento Cards
            { "CardCpuTitle", "CPU" },
            { "CardGpuTitle", "GPU" },
            { "CardNpuTitle", "NPU (AI)" },
            { "CardRamTitle", "RAM" },
            { "CardDiskTitle", "STORAGE" },
            { "CardNetTitle", "NETWORK" },

            { "GpuDiscreteBadge", "Discrete GPU" },
            { "GpuIntegratedBadge", "Integrated Graphics" },
            { "GpuDirect3DBadge", "3D Engine" },
            { "NpuIdleBadge", "Idle" },
            { "NpuActiveBadge", "Active" },
            { "NpuNotDetected", "Not Detected" },
            { "RamUsedLabel", "{0:N1} / {1:N1} GB Used" },
            { "DiskFreeLabel", "{0:N0} GB Free" },

            // Live Wave Charts
            { "WaveCpuTitle", "CPU LOAD (30s REAL-TIME WAVE)" },
            { "WaveNetTitle", "NETWORK THROUGHPUT (30s REAL-TIME WAVE)" },
            { "PeakLabel", "Peak" },

            // Deep Dive Tabs
            { "TabProcesses", "📊 Processes" },
            { "TabAccelerators", "⚡ Accelerators (GPU/NPU)" },
            { "TabServices", "⚙️ Services" },
            { "TabTasks", "⏱️ Scheduled Tasks" },
            { "TabStartup", "🚀 Startup Apps" },
            { "TabDrives", "💾 Storage & Drives" },

            { "TabProcessesSummary", "Top Running Processes" },
            { "TabAcceleratorsSummary", "GPU & NPU Accelerator Diagnostics" },
            { "TabServicesSummary", "System Services Status" },
            { "TabTasksSummary", "Scheduled Windows Tasks" },
            { "TabStartupSummary", "Startup Applications" },
            { "TabDrivesSummary", "Disk Volumes & Partitions" },

            // Process Controls & Filters
            { "SearchPlaceholder", "🔍 Search process by name or PID..." },
            { "SortByCpu", "⚡ Sort: CPU %" },
            { "SortByRam", "🧠 Sort: RAM MB" },
            { "UnresponsiveWarning", "⚠️ {0} Unresponsive Process(es)" },
            { "RescueUnresponsive", "Rescue / Close" },

            // Table & Column Headers
            { "ColPid", "PID" },
            { "ColApp", "APPLICATION" },
            { "ColCpu", "CPU %" },
            { "ColWorkingSet", "WORKING SET" },
            { "ColRamPercent", "% RAM" },
            { "ColState", "STATUS" },
            { "ColPriority", "PRIORITY" },
            { "ColActions", "ACTIONS" },
            { "ColServiceName", "WINDOWS SERVICE" },
            { "ColServiceStatus", "STATUS" },
            { "ColTaskName", "SCHEDULED TASK" },
            { "ColTaskState", "STATE" },
            { "ColStartupApp", "APPLICATION / PROGRAM" },
            { "ColStartupLocation", "LOCATION" },
            { "ColStartupStatus", "STATUS" },
            { "ColStartupActions", "ACTIONS" },

            // Hardware & Storage Deck Headers
            { "CardHardwareTitle", "SYSTEM & HARDWARE" },
            { "HwProcessor", "PROCESSOR" },
            { "HwGraphics", "GRAPHICS ADAPTER" },
            { "HwNpu", "NPU AI ENGINE" },
            { "HwOsLabel", "OPERATING SYSTEM" },
            { "HwPowerSchemeLabel", "POWER SCHEME" },
            { "StorageVolumes", "STORAGE VOLUMES" },

            // Accelerators Tab
            { "GpuDeckTitle", "GRAPHICS PROCESSOR (GPU / VRAM)" },
            { "Gpu3DRendering", "3D RENDERING" },
            { "GpuComputeML", "COMPUTE / ML" },
            { "GpuVideoDecode", "VIDEO DECODE" },
            { "GpuCopyEngine", "COPY ENGINE" },
            { "GpuVramTitle", "DEDICATED VIDEO MEMORY (VRAM)" },

            { "NpuDeckTitle", "NEURAL PROCESSING UNIT (NPU / AI ENGINE)" },
            { "NpuDeckDesc", "Dedicated low-power SoC accelerator for Windows Studio Effects, DirectML, and local AI model inference." },
            { "NpuComputeUtilization", "NEURAL COMPUTE ENGINE UTILIZATION" },

            // Context Menus & Action Tooltips
            { "MenuDetails", "View Process Details" },
            { "MenuSuspend", "⏸ Suspend Process" },
            { "MenuResume", "▶ Resume Process" },
            { "MenuPriority", "⚡ CPU Priority" },
            { "MenuOpenFolder", "Open File Location" },
            { "MenuSearchOnline", "Search Online" },
            { "MenuEndProcess", "End Process" },
            { "MenuOpenDrive", "Open Drive in Explorer" },
            { "MenuCleanDrive", "Clean Drive (Storage Sense)" },
            { "MenuServiceStart", "▶️ Start Service" },
            { "MenuServiceStop", "⏹️ Stop Service" },
            { "MenuServiceRestart", "🔄 Restart Service" },
            { "MenuOpenServicesMsc", "⚙️ Open Services Management (services.msc)" },
            { "MenuTaskRun", "▶️ Run Task Now" },
            { "MenuTaskEnd", "⏹️ End Task" },
            { "MenuOpenTaskSchd", "📅 Open Task Scheduler (taskschd.msc)" },
            { "MenuStartupOpenFolder", "📂 Open File Location" },
            { "MenuStartupSearch", "🔍 Search Program Online" },
            { "MenuStartupCopyCmd", "📋 Copy Command / Path" },
            { "MenuStartupSettings", "⚙️ Open Windows Startup Settings" },
            { "TooltipOpenFolder", "Open Folder" },
            { "TooltipSearchOnline", "Search Online" },
            { "TooltipOpenNativeTools", "System Tools" },

            // Mini Widget
            { "WidgetRestore", "🖥️ Restore to Full Mode" },
            { "WidgetSwitchHero", "📊 Switch to Compact Mode" },
            { "WidgetSnapBottomRight", "📍 Snap to Bottom-Right Corner" },
            { "WidgetPinAlways", "📌 Pin Always on Top" },
            { "WidgetClose", "❌ Close Monitor" },
            { "WidgetTooltip", "Double-click to restore Full Mode (or right-click for options)" },

            // Toast Messages
            { "ToastRamTrimmed", "⚡ RAM Optimized ({0} processes trimmed)" },
            { "ToastTempCleaned", "🧹 Cleaned {0} in Deep Clean ({1} files)" },
            { "ToastTurboMode", "🚀 Turbo Mode Activated! (High Performance + RAM Purge)" },
            { "ToastDnsFlushed", "🌐 DNS resolver cache flushed successfully!" },
            { "ToastSnapshotCopied", "📸 Diagnostic Snapshot Copied to Clipboard!" },
            { "ToastPowerPlan", "⚡ Power Plan: {0}" },
            { "ToastTheme", "🎨 Theme: {0}" },
            { "ToastInterval", "⏱️ Interval: {0}s" },
            { "ToastPinned", "📌 Window Pinned on Top" },
            { "ToastUnpinned", "📌 Window Unpinned" },
            { "ToastWidgetDocked", "📍 Widget Docked to Bottom-Right Corner" },
            { "ToastProcessEnded", "🔴 Process Ended: {0}" },
            { "ToastProcessSuspended", "⏸ Process Suspended: {0}" },
            { "ToastProcessResumed", "▶ Process Resumed: {0}" },
            { "ToastPriorityChanged", "⚡ Priority for {0} changed to {1}" },
            { "ToastServiceStarted", "▶️ Service Started: {0}" },
            { "ToastServiceStopped", "⏹️ Service Stopped: {0}" },
            { "ToastTaskExecuted", "📅 Task Executed: {0}" },
            { "ToastStartupCopied", "📋 Path copied to clipboard" }
        };

        public static string Get(string key, string langOrFallback = null)
        {
            string targetLang = CurrentLanguage;
            string fallback = key;

            if (!string.IsNullOrEmpty(langOrFallback))
            {
                if (string.Equals(langOrFallback, "es", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(langOrFallback, "en", StringComparison.OrdinalIgnoreCase))
                {
                    targetLang = langOrFallback;
                }
                else
                {
                    fallback = langOrFallback;
                }
            }

            var dict = string.Equals(targetLang, "en", StringComparison.OrdinalIgnoreCase) ? StringsEn : StringsEs;

            string value;
            if (dict.TryGetValue(key, out value))
            {
                return value;
            }

            // Fallback to English dictionary
            if (StringsEn.TryGetValue(key, out value))
            {
                return value;
            }

            // Fallback to Spanish dictionary
            if (StringsEs.TryGetValue(key, out value))
            {
                return value;
            }

            return fallback;
        }
    }
}
