> **Created:** 2026-08-31
> **Last Updated:** 2026-08-31

# AI Agent Process Hierarchies, MCP Subprocess Architecture & Two-Phase Process Termination on Windows

## 1. Overview & Problem Definition

Modern software development with AI agents (Claude Code, Gemini CLI, Codex, Aider, Open-Interpreter, Cursor, Copilot) relies on distributed multi-process architectures running locally. A parent CLI / orchestrator process spawns multiple long-lived and ephemeral child processes, predominantly Model Context Protocol (MCP) servers (running over `node.exe`, `python.exe`, `uvx.exe`, `docker.exe`, or custom executables).

Traditional Windows task managers fail in two critical ways:
1. **Metric Fragmentation**: Child processes are displayed disconnected from their orchestrator root, obscuring total aggregated RAM (often 1.5–3.5 GB) and CPU consumption.
2. **Destructive Termination & Zombie Leaks**: Simple `taskkill /F` or `Process.Kill()` breaks graceful state persistence (SQLite locks, git indexes, session state) while naive `WM_CLOSE` fails because desktop apps minimize to the Windows System Tray rather than terminating.

---

## 2. Windows Kernel Process Hierarchy Resolution Benchmark

### A. Evaluated Techniques
- **WMI (`Win32_Process`)**: `SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process`
  - Latency: **120 ms – 350 ms** per call. High COM allocations. Unsuitable for telemetry loops.
- **Win32 Toolhelp32 Snapshot (`CreateToolhelp32Snapshot`)**:
  - P/Invoke: `kernel32.dll!CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)` -> `Process32First` / `Process32Next`.
  - Latency: **< 0.8 ms** for 250+ processes.
  - Memory: Flat array traversal, zero heap allocation overhead.
  - Returns `th32ProcessID` and `th32ParentProcessID` in an atomic kernel snapshot.
- **Native NT API (`NtQueryInformationProcess`)**:
  - Requires opening a handle to every individual process (`OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`), yielding higher cumulative latency (~10 ms) and security descriptor friction.

### B. Selected Architecture
**Win32 Toolhelp32 Snapshot** is selected for Simple PC Monitor. It enables $O(N)$ parent-child tree reconstruction in a single synchronous pass without external dependencies or UAC elevation prompts for basic process trees.

---

## 3. AI Agent & MCP Signatures Matrix

| Agent / Tool | Root Process Name | Typical CLI Parameters | Child Process Ecosystem |
| :--- | :--- | :--- | :--- |
| **Claude Code CLI** | `claude.exe`, `claude` | `--output-format stream-json`, `--resume=<uuid>` | `node.exe` (MCP servers), `git.exe`, `rg.exe` |
| **Gemini CLI** | `gemini.exe`, `gemini` | `cli`, `--model` | `python.exe`, `node.exe`, `uvx.exe` (MCP servers) |
| **Aider** | `aider.exe`, `python.exe` | `aider`, `main.py` | `git.exe`, `python.exe` |
| **Codex / Open Interpreter**| `codex.exe`, `interpreter.exe`| `cli`, `--api-key` | `python.exe`, `uv.exe` |
| **Ollama** | `ollama.exe`, `ollama_app.exe`| `serve`, `run` | `ollama_llama_server.exe` |
| **Cursor / VSCode AI** | `cursor.exe`, `Code.exe` | `--ms-enable-electron-run-as-node` | Language servers, MCP connectors |

### Aggregated Resource Formula:
$$\text{RAM}_{\text{session}} = \text{WorkingSet64}(\text{Parent}) + \sum_{i \in \text{Children}} \text{WorkingSet64}(i)$$
$$\text{CPU\%}_{\text{session}} = \text{CPU\%}(\text{Parent}) + \sum_{i \in \text{Children}} \text{CPU\%}(i)$$

### Active vs. Idle Heuristic:
- Background MCP heartbeats consume $0.01 - 0.03$ cores in 30s.
- Threshold: $\Delta \text{CPU} < 0.04$ cores $\rightarrow$ **Idle / Inactive**; $\Delta \text{CPU} \ge 0.04$ cores $\rightarrow$ **Active / Processing**.

---

## 4. Two-Phase Graceful Process Termination Protocol

### Phase 1: Graceful Close & Window State Assessment
1. Check process against protected system blacklist and Session 0 isolation.
2. For GUI applications: Dispatch `Process.CloseMainWindow()` / `PostMessage(hWnd, WM_CLOSE, 0, 0)`.
3. For CLI/Console tools: Dispatch `GenerateConsoleCtrlEvent(CTRL_C_EVENT, pid)` / `CTRL_BREAK_EVENT` via `AttachConsole`.
4. Asynchronously await exit (`Task.Delay(1500-2000)`).
5. State Evaluation:
   - `proc.HasExited == true`: Clean termination confirmed.
   - `proc.HasExited == false` and `proc.MainWindowHandle == IntPtr.Zero` (or `IsWindowVisible(hWnd) == false`): **App minimized to System Tray / background service**.
   - `proc.HasExited == false` and window remains open: **App busy, hanging, or prompt blocked**.

### Phase 2: Escalated Force Kill
- If the application remains active and the user requests termination:
  - For standalone processes: `proc.Kill()`.
  - For AI Agent trees: Traverse process tree in reverse topological order (leaves/MCP children first, then root parent) to avoid orphaned subprocesses.

---

## 5. References & Documentation Links
- [Microsoft Docs - CreateToolhelp32Snapshot](https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/nf-tlhelp32-createtoolhelp32snapshot)
- [Microsoft Docs - Process Tree Management in Win32](https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags)
- [Model Context Protocol Specification](https://modelcontextprotocol.io/)
- [Win32 Console Control Handlers & Signaling](https://learn.microsoft.com/en-us/windows/console/handlerroutine)
