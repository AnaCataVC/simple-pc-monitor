> **Created:** 2026-08-31
> **Last Updated:** 2026-08-31
> **Status:** Active
> **Scope:** AI Agent & MCP Monitor / Two-Phase Process Termination

# Adversarial Stress-Test & Premortem Analysis

## 1. Premortem & Operational Failure Modes

### 💥 Falla Catastrófica 1: Fuga de Handles de Kernel por Snapshots Continuos
- **Mecanismo:** Si `CreateToolhelp32Snapshot` no libera su handle mediante `CloseHandle` en un bloque `try/finally` absoluto (por ejemplo, si ocurre una excepción de conversión dentro del bucle `Process32Next`), la aplicación fugará 1800 handles por hora (1 cada 2 segundos). En 48 horas de ejecución continua en la bandeja del sistema, Simple PC Monitor saturará los recursos de handles de Windows y crasheará con `OutOfMemoryException` / fallo del subsistema GDI/Kernel.
- **Severidad:** **[Critical / Blocker]**
- **Mitigación Mandatoria:**
  ```csharp
  IntPtr hSnapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
  if (hSnapshot != IntPtr.Zero && hSnapshot != new IntPtr(-1))
  {
      try { /* Recorrido Process32First / Process32Next */ }
      finally { NativeMethods.CloseHandle(hSnapshot); }
  }
  ```

---

### 💥 Falla Catastrófica 2: Bloqueo de Consola en `AttachConsole` / `GenerateConsoleCtrlEvent`
- **Mecanismo:** Un proceso en Windows solo puede conectarse a una única consola a la vez. Si dos hilos de Simple PC Monitor intentan hacer `AttachConsole` en paralelo o si el proceso CLI objetivo está en otro nivel de integridad/sandbox o ya murió:
  1. `AttachConsole` falla con `ERROR_ACCESS_DENIED` o `ERROR_INVALID_HANDLE`.
  2. Si no se llama `FreeConsole()` inmediatamente tras el envío, Simple PC Monitor queda permanentemente enganchado a una consola ajena, rompiendo futuros cierres.
  3. `GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)` propaga la señal a **todos** los procesos que compartan esa consola, pudiendo matar terminales activas de PowerShell o CMD del usuario.
- **Severidad:** **[Critical / Blocker]**
- **Mitigación Mandatoria:**
  - Proteger toda la operación `AttachConsole` con un cerrojo exclusivo (`lock (_consoleLock)`).
  - Envolver siempre en `try / finally` que garantice la ejecución de `FreeConsole()`.
  - Usar `GenerateConsoleCtrlEvent(CTRL_C_EVENT, (uint)pid)` con el PID objetivo específico en lugar de 0 (broadcast global).
  - Fallback inmediato: Si `AttachConsole` retorna `false`, recurrir limpiamente a `Process.CloseMainWindow()` / `WM_CLOSE` sin propagar excepciones.

---

## 2. Concurrency, Race Conditions & State Drift

### ⚡ Vector 3: Falsos Positivos por Reciclaje de PIDs (PID Reuse Collision)
- **Mecanismo:** En Windows los PIDs se reciclan rápidamente. Si una sesión de `claude.exe` muere y Windows reasigna su PID = 14200 a `notepad.exe` o `svchost.exe`, un proceso MCP secundario (`node.exe`) cuyo `th32ParentProcessID` era 14200 quedaría erróneamente vinculado a la nueva aplicación.
- **Severidad:** **[Major / Hardening Required]**
- **Mitigación Mandatoria (Triple Verificación Invariante):**
  1. `child.StartTime >= parent.StartTime` (Un hijo jamás puede haber nacido antes que su padre).
  2. Verificar que el proceso padre tenga una firma autorizada de agente (`claude`, `gemini`, `codex`, `aider`, `cursor`, `ollama`).
  3. Si el proceso padre ya no existe o cambió su firma, clasificar al proceso hijo como **"Servidor MCP Huérfano"** (Orphaned), desvinculándolo de cualquier nuevo proceso con el mismo PID.

---

## 3. Cost & Resource Explosion

### 📈 Vector 4: Excepciones Masivas de Acceso Denegado (Antivirus / PPL Processes)
- **Mecanismo:** Procesos protegidos por el sistema o por soluciones de seguridad (PPL, Antivirus, Anticheats como Vanguard) lanzan `Win32Exception (0x80004005: Access Denied)` al intentar leer `p.StartTime`, `p.TotalProcessorTime` o `p.MainWindowHandle`. Si estas consultas no están aisladas por bloques `try/catch` granulares por propiedad, la iteración completa de procesos se abortará en cada ciclo de 2 segundos.
- **Severidad:** **[Major / Hardening Required]**
- **Mitigación Mandatoria:**
  - Implementar getters seguros con fallback (`TryGetStartTime`, `TryGetTotalProcessorTime`, `TryGetMainWindowHandle`) que capturen `Win32Exception` e `InvalidOperationException` individualmente sin interrumpir el flujo.

---

## 4. Security & Abuse Vectors

### 🛡️ Vector 5: Intento de Cierre de Procesos en Sesión 0 o Blacklist
- **Mecanismo:** Si un subproceso hijo reporta un PID del sistema (por ejemplo, si un CLI invoca un servicio del sistema o un proceso en Sesión 0), la terminación en cascada podría intentar matar `services.exe` o `svchost.exe`.
- **Severidad:** **[Critical / Blocker]**
- **Mitigación Mandatoria:**
  - El filtro `ProcessManager.IsSafeToControl(pid, name)` debe ser evaluado **recursivamente sobre cada nodo del árbol** antes de emitir cualquier señal de cierre o terminación.

---

## 5. Matriz Resumen de Vulnerabilidades y Contramedidas

| Vector de Ataque | Severidad | Riesgo en Producción | Contramedida de Hardening Aprobada |
| :--- | :--- | :--- | :--- |
| **Fuga de Handles `CreateToolhelp32Snapshot`** | 🔴 **Critical** | Agotamiento de handles del OS en 48h | `try / finally` estricto con `CloseHandle` garantizado. |
| **Broadcast no deseado en `AttachConsole`** | 🔴 **Critical** | Cierre accidental de consolas compartidas | `lock` de sincronización + `FreeConsole()` en `finally` + PID target explícito. |
| **Colisión por Reciclaje de PIDs** | 🟠 **Major** | Asignar procesos ajenos a sesiones de IA | Validación `child.StartTime >= parent.StartTime` + verificación de firma. |
| **Access Denied en Procesos Protegidos** | 🟠 **Major** | Cancelación del bucle de telemetría | Lectura granular con `try/catch` individual por propiedad Win32. |
| **Árboles huérfanos invertidos** | 🟡 **Minor** | Fuga de memoria por MCPs zombis | Terminación en **orden topológico inverso** (hijos primero, raíz al final). |
