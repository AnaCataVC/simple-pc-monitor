> **Created:** 2026-08-31
> **Last Updated:** 2026-09-01

# AI Agent Process Hierarchies, MCP Subprocess Architecture & Two-Phase Process Termination on Windows

## 1. Overview & Problem Definition

Modern software development with AI coding agents (Google Antigravity, Claude Code, Gemini CLI, OpenAI Codex, Aider, Cursor, Windsurf, Cline, Roo Code, Copilot, LM Studio, Ollama) relies on distributed multi-process architectures running locally on developer workstations. An orchestrator parent process spawns multiple long-lived and ephemeral child processes, predominantly Model Context Protocol (MCP) servers (running over `node.exe`, `python.exe`, `uvx.exe`, `bun.exe`, `deno.exe`, `docker.exe`, or specialized CLI utilities).

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

## 3. Verified AI Agent & MCP Signatures Matrix (2026 Full Catalog)

### A. AI Agent & Coding Orchestrators
| Agent / Tool | Root Executable Signatures | Category / Runtime | Typical Child Ecosystem |
| :--- | :--- | :--- | :--- |
| **Google Antigravity** | `Antigravity.exe`, `agy.exe` | AI IDE / CLI Orchestrator | Subagent workers, `node.exe` MCP servers, `rg.exe`, `git.exe` |
| **Claude Code & Desktop** | `claude.exe`, `claude-code.exe` | CLI / Electron Desktop | Multiple MCP servers (`node.exe`, `python.exe`, `uvx.exe`), `bash.exe`, `rg.exe` |
| **Gemini CLI** | `gemini.exe`, `gemini-cli.exe` | Python / Go CLI | `python.exe`, `uvx.exe`, MCP connector subprocesses |
| **Cursor AI IDE** | `cursor.exe` | Electron / VS Code Fork | Extension hosts, language servers, MCP server child trees |
| **Windsurf AI IDE** | `Windsurf.exe` | Electron / Codeium Fork | Cascade agent workers, language servers, MCP tools |
| **Aider Pair Programming**| `aider.exe`, `python.exe` | Python CLI Pair Programmer | `git.exe`, local language servers, diff tools |
| **OpenAI Codex / ChatGPT**| `codex.exe`, `chatgpt.exe` | Native / Electron Client | Search helpers, sandbox runners |
| **Ollama Local LLM** | `ollama.exe`, `ollama app.exe` | Go / C++ Local Inference Server | `ollama_llama_server.exe`, model worker backends |
| **LM Studio Local LLM** | `LM Studio.exe`, `lms.exe` | Electron / CLI Inference Host | Headless inference servers, local embeddings workers |
| **Cline & Roo Code** | `cline.exe`, `roo-code.exe`, `roo.exe` | AI Developer Extension / CLI | Node.js MCP tools, shell subprocesses |
| **GitHub Copilot** | `copilot.exe`, `copilot-agent.exe` | Language Server / CLI | `copilot-language-server`, Node.js runners |
| **Open Interpreter** | `interpreter.exe`, `open-interpreter.exe`| Python CLI Interpreter | Terminal shells, Python execution kernels |
| **LocalAI** | `localai.exe` | Multi-model Local Backend | gRPC worker backends, Python runners |
| **Continue & Cody** | `continue.exe`, `cody.exe` | Extension Agents | Local embedding servers, indexing subprocesses |

### B. Recognized MCP Runtimes & Subprocesses
| Subprocess Name | Classification & Purpose |
| :--- | :--- |
| `node.exe` | Model Context Protocol (MCP) Server running TypeScript / JavaScript tool servers |
| `python.exe`, `python3.exe`, `pythonw.exe` | MCP Server running Python FastMCP / SDK servers |
| `uvx.exe`, `uv.exe` | Astral uv / uvx ephemeral MCP server runners |
| `npx.cmd`, `npx.exe` | Node Package Execute MCP runners |
| `bun.exe`, `deno.exe` | Fast JS/TS alternative runtimes for MCP servers |
| `docker.exe`, `dockerd.exe` | Isolated containerized MCP tool environments |
| `rg.exe` | Ripgrep high-performance workspace grep tool |
| `git.exe` | Source control inspection and patch generation |
| `pwsh.exe`, `powershell.exe`, `cmd.exe` | Terminal shell executors spawned by autonomous agents |

---

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
- [MR-Axel/monitor-sistema Architecture (Research Reference)](https://github.com/MR-Axel/monitor-sistema)
