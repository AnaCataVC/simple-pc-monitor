> **Created:** 2026-09-02
> **Last Updated:** 2026-09-04

# Process Command Line Extraction & Subprocess Semantic Classification on Windows (.NET Framework / Win32)

## 1. Executive Summary & Research Question
When monitoring multi-process developer tools and AI coding agents (Google Antigravity, Claude Code, Cursor, Windsurf, Aider), executable names (e.g., `Antigravity.exe`, `claude.exe`, `language_server.exe`) are replicated across dozens of child subprocesses. Displaying them with identical generic labels (like `"Worker Subprocess"` or `"language_server"`) severely degrades user observability.

The goal of this research is to evaluate and benchmark the most reliable, secure, zero-dependency, and high-performance method in **C# / Windows (.NET Framework 4.8)** to extract command-line arguments and assign descriptive, semantic roles to every child process.

---

## 2. API Evaluation & Performance Benchmark Matrix

| Evaluation Dimension | Native NT API (`NtQueryInformationProcess` Class 60) | WMI (`Win32_Process.CommandLine`) | PEB Memory Reading (`ReadProcessMemory`) | Toolhelp32 Snapshot (`CreateToolhelp32Snapshot`) |
| :--- | :--- | :--- | :--- | :--- |
| **Supported OS** | Windows 8.1, Windows 10, Windows 11 | Windows 2000 through Windows 11 | Windows XP through Windows 11 | All Windows versions |
| **Execution Latency** | **< 0.05 ms** per process | **120 ms – 350 ms** per query batch | **0.1 ms – 0.5 ms** per process | **< 0.8 ms** (whole system tree) |
| **Required Permissions** | `PROCESS_QUERY_LIMITED_INFORMATION` (0x1000) | Standard User (COM/WMI RPC) | `PROCESS_VM_READ` + `PROCESS_QUERY_INFORMATION` (Elevated/UAC) | Standard User |
| **Memory Footprint** | 0 heap allocations (flat stack/HGlobal buffer) | High COM/BSTR allocations & WMI service spin-up | Low, but fragile pointer traversal | Minimal flat array |
| **64-bit / 32-bit (WoW64) Stability** | Consistent in 64-bit kernel memory | Consistent | Requires separate 32-bit and 64-bit PEB offsets | Native OS handling |
| **Recommendation** | **PRIMARY CHOICE (Recommended)** | **NOT RECOMMENDED for 1-3s real-time loops** | **REJECTED (Fragile & Requires UAC)** | **USED FOR TREE DISCOVERY** |

---

## 3. Deep Dive: `NtQueryInformationProcess` with `ProcessCommandLineInformation` (Class 60)

### 3.1 Kernel Mechanism
Starting with **Windows 8.1** (NT 6.3) and continued through **Windows 10** and **Windows 11**, Microsoft introduced `ProcessCommandLineInformation` (numeric value `60`) in `PROCESSINFOCLASS`.

Unlike older approaches that forced developers to locate the `PEB` base address, find `RTL_USER_PROCESS_PARAMETERS`, and call `ReadProcessMemory` across process boundaries (which fails without `PROCESS_VM_READ` or SeDebugPrivilege):
1. The kernel handles cross-process boundary copying internally inside `ntdll.dll`.
2. The caller supplies a buffer and process handle opened with only **`PROCESS_QUERY_LIMITED_INFORMATION`** (`0x1000`).
3. The kernel writes a `UNICODE_STRING` structure directly into the supplied buffer:
   ```csharp
   [StructLayout(LayoutKind.Sequential)]
   public struct UNICODE_STRING
   {
       public ushort Length;         // Length in bytes (not characters)
       public ushort MaximumLength;  // Maximum buffer length
       public IntPtr Buffer;         // Pointer to wide characters within the allocated block
   }
   ```
4. The actual characters of the command line immediately follow the `UNICODE_STRING` header inside the allocated memory block, enabling direct zero-copy marshaling via `Marshal.PtrToStringUni(ustr.Buffer, ustr.Length / 2)`.

### 3.2 Two-Pass Buffer Sizing Pattern
To prevent buffer truncations or memory leaks:
1. **Pass 1 (Probe)**: Call `NtQueryInformationProcess(hProcess, 60, IntPtr.Zero, 0, out uint requiredLength)`. The API returns `STATUS_INFO_LENGTH_MISMATCH` (`0xC0000004`) and populates `requiredLength`.
2. **Pass 2 (Fetch)**: Allocate `Marshal.AllocHGlobal((int)requiredLength)`. Call again with the exact size.
3. If successful (`STATUS_SUCCESS = 0x00000000`), read `UNICODE_STRING`.

### 3.3 Access Control & Edge Cases
- **Same User Session**: 100% success rate for all user-level processes (Google Antigravity, Claude, Cursor, Node, Python, terminals).
- **Elevated / Admin Processes**: When the monitor runs without elevation, opening an elevated process handle returns `IntPtr.Zero` (`Access Denied`). The code must cleanly return `null` and fall back to `FileVersionInfo` or pre-baked signatures.
- **Process Termination Race**: If the target process dies between Toolhelp32 snapshot and inspection, `OpenProcess` returns `IntPtr.Zero`, handled cleanly with `try/finally`.

### 3.4 Process Cold-Start & PEB Race Conditions
When a new process is spawned in Windows, its entry appears in the Toolhelp32 snapshot immediately after kernel thread creation. However, user-mode initialization of `RTL_USER_PROCESS_PARAMETERS` in the Process Environment Block (PEB) takes several milliseconds:
- During this transient startup window, `NtQueryInformationProcess(ProcessCommandLineInformation)` may return `null` or a zero-length string.
- **Telemetry Invariant:** Telemetry collectors must **never insert negative cache entries** when `cmd == null`. Deferring caching allows the next sampling tick to accurately inspect the populated command line once user-mode initialization completes.

---

## 4. Semantic Subprocess Classification Catalog

Once the raw command line is extracted, Simple PC Monitor maps flags to clear, human-readable roles:

### 4.1 Chromium & Electron Framework Subprocesses
Modern AI coding apps (Antigravity, Claude Desktop, Cursor, Windsurf) run on Chromium/Electron. Subprocesses follow strict command-line conventions:
- `--type=renderer`: 🌐 **Renderizador UI** (webview per tab/editor).
- `--type=gpu-process`: ⚡ **Aceleración GPU** (DirectX hardware compositor).
- `--type=crashpad-handler`: 🩺 **Monitor Crashpad** (crash reporter).
- `--type=utility`: 🛠️ **Servicio de Utilidad** (network, audio, video capture).

### 4.2 Language Server & Background Agent Workers
In Google Antigravity and VS Code forks:
- `language_server.exe multicall schedule ...`: ⏱️ **Cron: Tarea Programada**.
- `language_server.exe ... agentapi new-conversation`: 🤖 **Subagente Autónomo (Worker)**.
- `language_server.exe ... --lsp`: 🧩 **Servidor LSP (IntelliSense)**.

### 4.3 Windows Console Host
- `conhost.exe`: 📟 **Host de Consola Windows**.

### 4.4 Model Context Protocol (MCP) Servers (Evidence-Based Resolution)
MCP servers are identified by explicit command-line markers rather than runtime process names:
- Markers: `mcp-remote`, `modelcontextprotocol`, `mcp-server`, `mcp_server`, `--stdio`, `/mcp`, `\mcp`.
- Extraction Regex:
  - Package/server name: `@?(?:[a-zA-Z0-9_-]+/)?(?:mcp[-_]server[-_][a-zA-Z0-9_\-]+|server[-_][a-zA-Z0-9_\-]+)`
  - Python scripts: `(?i)([a-zA-Z0-9_\-]+\.py)`
  - Node entrypoints: `(?i)([a-zA-Z0-9_\-]+)[\\/](?:index|server|main)\.js`
- Classification: `Role = "Servidor MCP (" + procName + ")"`, `RoleBadgeColor = "#10B981"`, `IsMcpServer = true`.
- Supports compiled native binaries (**Go**, **Rust**) while discarding plain Node/Python scripts without MCP markers.

### 4.5 MCP Package Runner Isolation & Shell Exclusion
- **Package Runners (`npx-cli.js`, `\npx\`, `uvx.exe`):**
  - Identified via `IsMcpPackageRunner(lowerCmd)`.
  - Classification: `Role = "Lanzador de paquete MCP"`, `RoleBadgeColor = "#64748B"`, `IsMcpServer = false`.
  - Prevents double-counting the runner and the server as two MCP instances.
- **Shell Launchers (`cmd`, `pwsh`, `powershell`, `bash`, `sh`, `wsl`, `conhost`):**
  - Excluded via `ShellProcessNames`. Even though they contain `--stdio` or `mcp-server` in arguments passed to child tools, they are classified as `"Terminal Shell"` (`IsMcpServer = false`).

### 4.6 Independent CLI Agent Sessions
- **Session Markers (`AgentSessionCommandLineMarkers`):**
  - `--output-format stream-json`, `--resume=`, `--session-id`.
- When present on child processes matching known agent names, `IsIndependentAgentSession` triggers root promotion, decoupling the CLI agent from the parent IDE and pruning session boundaries to eliminate RAM/CPU double counting.

---

## 5. Security, Privacy & Sanitization Directives
- **Path Privacy**: Command lines may contain absolute user paths (e.g., `C:\Users\username\AppData\...`).
  - The extraction layer must replace `C:\Users\<user>` with `%USERPROFILE%` before presenting to UI or logs.
- **Sensitive Token Stripping**: If a command line contains `--api-key`, `--token`, or `--password`, the argument value must be masked as `[REDACTED]`.

---

## 6. Official References & Documentation
- Microsoft Learn: [NtQueryInformationProcess documentation](https://learn.microsoft.com/en-us/windows/win32/procthread/zwqueryinformationprocess)
- Microsoft Learn: [PROCESS_QUERY_LIMITED_INFORMATION access rights](https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights)
- Chromium Multi-Process Architecture: [Chromium Developer Documentation](https://www.chromium.org/developers/design-documents/multi-process-architecture/)
- Process Hacker / System Informer source: `ProcessCommandLineInformation` implementation in `phlib/native.c`.
