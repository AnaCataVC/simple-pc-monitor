> **Created:** 2026-08-31
> **Last Updated:** 2026-09-04

# AI Agent Process Hierarchies, MCP Subprocess Architecture & Two-Phase Process Termination on Windows

## 1. Overview & Problem Definition

Modern software development with AI coding agents (Google Antigravity, Claude Code, Gemini CLI, OpenAI Codex, Aider, Cursor, Windsurf, Cline, Roo Code, Copilot, LM Studio, Ollama) relies on distributed multi-process architectures running locally on developer workstations. An orchestrator parent process spawns multiple long-lived and ephemeral child processes, predominantly Model Context Protocol (MCP) servers (running over `node.exe`, `python.exe`, compiled native binaries in Go/Rust, `uvx.exe`, `bun.exe`, `deno.exe`, `docker.exe`, or specialized CLI utilities).

Traditional Windows task managers fail in three critical ways:
1. **Metric Fragmentation & Conflation**: Child processes are displayed disconnected from their orchestrator root, obscuring total aggregated RAM (often 1.5–3.5 GB) and CPU consumption. Concurrently, naive tools conflate generic helper subprocesses (renderers, builds, language servers) with verified tool servers.
2. **Double-Counting & Process Duplication**: Persistent package runners (`npx`, `uvx`) stay resident alongside target servers, leading to double-counting. Similarly, autonomous subagents spawned inside IDEs are counted both in the IDE and in child session views without boundary truncation.
3. **Destructive Termination & Zombie Leaks**: Simple `taskkill /F` or `Process.Kill()` breaks graceful state persistence (SQLite locks, git indexes, session state) while naive `WM_CLOSE` fails because desktop apps minimize to the Windows System Tray rather than terminating.

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

### B. Command-Line Evidence-Based MCP Detection & Subprocess Roles
Subprocess classification is governed strictly by **command-line evidence**, not by executable names alone:
1. Compiled native MCP servers (**Go**, **Rust**, **C++**) are correctly identified even though their process names are not generic runtimes.
2. Generic runtime processes (`node.exe`, `python.exe`) hosting build tools, linters, or language servers without MCP arguments are classified as helper processes (`IsMcpServer = false`) and not counted as MCP servers.

#### 1. MCP Command-Line Evidence Markers:
- `mcp-remote`, `modelcontextprotocol`, `mcp-server`, `mcp_server`, `--stdio`, `/mcp`, `\mcp`.

#### 2. MCP Package Runner Deduplication:
Ephemeral package tools (`npx`, `uvx`) spawn the target MCP server but remain running as supervisor parents:
- Markers: `npx-cli.js`, `\npx\`, `uvx.exe`.
- Semantic Role: `"Lanzador de paquete MCP"` (`RoleBadgeColor = #64748B`, `IsMcpServer = false`).
- Prevents counting 1 logical MCP server as 2 instances.

#### 3. Shell Launcher Filtering:
Command line arguments pass through shell launchers (`cmd`, `pwsh`, `powershell`, `bash`, `sh`, `wsl`, `conhost`). These executables match MCP markers in their arguments but are strictly excluded via `ShellProcessNames` and marked as `"Terminal Shell Process"` (`IsMcpServer = false`).

#### 4. Subprocess Classification Summary:
| Subprocess Pattern / Marker | Role | IsMcpServer | Badge Color |
| :--- | :--- | :--- | :--- |
| Any binary + `McpCommandLineMarkers` | Model Context Protocol Server | `true` | `#10B981` (Emerald) |
| `npx-cli.js`, `uvx.exe` (`McpPackageRunnerMarkers`) | Lanzador de paquete MCP | `false` | `#64748B` (Slate) |
| `cmd`, `pwsh`, `bash`, `wsl`, `conhost` | Terminal Shell / Console Host | `false` | `#64748B` (Slate) |
| `node.exe` (no MCP markers) | Node.js Process (Build / Tool) | `false` | `#38BDF8` (Sky Blue) |
| `python.exe` (no MCP markers) | Python Process (Auxiliary Script)| `false` | `#38BDF8` (Sky Blue) |
| `--type=renderer` | Renderizador UI / Web (Chromium) | `false` | `#38BDF8` (Sky Blue) |
| `--type=gpu-process` | Aceleración GPU (DirectX Compositor)| `false` | `#A855F7` (Purple) |
| `language_server.exe multicall schedule` | Tarea Programada (Cron Planner) | `false` | `#6366F1` (Indigo) |
| `language_server.exe agentapi new-conversation`| Subagente Worker Autónomo | `false` | `#8B5CF6` (Violet) |
| `rg.exe` / `git.exe` | Ripgrep Search / Git Subprocess | `false` | `#E11D48` / `#F97316` |

### C. Independent CLI Agent Sessions & Boundary-Pruned Resource Aggregation
When an AI IDE (such as Google Antigravity, Cursor, or Windsurf) spawns an autonomous CLI agent (`claude`, `gemini`, `agy`), the CLI process is technically a child of the IDE. Simple PC Monitor resolves this via **Two-Tier Session Boundary Pruning**:
1. **Root Promotion Gate (`IsIndependentAgentSession`):**
   Subprocesses matching `KnownAgentSignatures` are evaluated for session markers (`--output-format stream-json`, `--resume=`, `--session-id`). If present, the process is promoted to `rootAgentPids`.
2. **Boundary-Cut Descendant Traversal (`CollectDescendants`):**
   The set of promoted roots (`rootPidSet`) is passed as `sessionBoundaries`. If descendant traversal encounters a PID in `sessionBoundaries`, traversal stops for that branch.

#### Consolidated Formula with Boundary Pruning:
$$\text{Descendants}(R) = \text{Tree}(R) \setminus \bigcup_{S \in \text{Roots}, S \neq R} \text{Tree}(S)$$
$$\text{RAM}_{\text{session}}(R) = \text{WS}(R) + \sum_{i \in \text{Descendants}(R)} \text{WS}(i)$$

### D. Metric Decoupling:
- `session.ChildProcessCount = session.ChildProcesses.Count;` (total descendant process count).
- `session.McpServersCount = session.ChildProcesses.Count(c => c.IsMcpServer);` (verified MCP tool servers).

### Active vs. Idle Heuristic:
- Background MCP heartbeats consume $0.01 - 0.03$ cores in 30s.
- Threshold: $\Delta \text{CPU} < 0.04$ cores $\rightarrow$ **Idle / Inactive** (`#64748B` Slate); $\Delta \text{CPU} \ge 0.04$ cores $\rightarrow$ **Active / Processing** (`#10B981` Emerald).

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

## 5. Quality, Concurrency & Lifecycle Invariants

### A. Deterministic Win32 Handle Disposal (`SafeProcessHandle`)
Every instantiation of `Process.GetProcessById()` acquires an underlying Win32 `SafeProcessHandle`. In telemetry collectors executing every 1–2 seconds, unmanaged handle table growth degrades OS responsiveness. Simple PC Monitor guarantees:
- Every `Process` instance in `AiAgentCollector.cs` is wrapped in scoped `using (...)` blocks or disposed in `finally`.
- Zero handle leaks validated over 200 consecutive sampling passes.

### B. Anti-Reentrancy Concurrency Gate (`_sampleGate`)
To prevent concurrent manual UI refreshes and periodic timer ticks from computing distorted delta fractions against `_prevCpuSamples`, a private `_sampleGate` re-entrancy lock serializes `Sample()` execution passes.

### C. Process Cold-Start Defense (PEB Initialization Timing)
Windows requires several milliseconds after process creation to map and populate the user-mode PEB (`RTL_USER_PROCESS_PARAMETERS`). If `NtQueryInformationProcess` returns `null` for the command line during this startup window, `IsIndependentAgentSession` returns `false` without caching the result, ensuring the process is properly re-evaluated once initialized.

### D. PID Recycling & Collapsed State Synchronization
When processes exit, dead PIDs are purged from `CollapsedSessionPids`, ensuring newly launched processes never inherit stale UI collapsed/expanded states.

---

## 6. References & Documentation Links

### A. Windows Kernel & Win32 APIs
- [Microsoft Learn - CreateToolhelp32Snapshot Function](https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/nf-tlhelp32-createtoolhelp32snapshot)
- [Microsoft Learn - Process Tree & Creation Flags in Win32](https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags)
- [Microsoft Learn - Console Control Handlers & GenerateConsoleCtrlEvent](https://learn.microsoft.com/en-us/windows/console/handlerroutine)
- [Microsoft Learn - AttachConsole Function & Process Signaling](https://learn.microsoft.com/en-us/windows/console/attachconsole)
- [Microsoft Learn - Windows Desktop App Execution Aliases](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions)

### B. AI Coding Agents & Developer IDEs
- [Anthropic - Claude Code Architecture & CLI Specifications](https://docs.anthropic.com/claude/docs/claude-code)
- [Google - Antigravity IDE & Autonomous Agent Framework](https://deepmind.google/technologies/)
- [Codeium - Windsurf AI IDE Process Architecture](https://codeium.com/windsurf)
- [Cursor - Cursor AI IDE Architecture & Electron Processes](https://www.cursor.com/)
- [Aider - AI Pair Programming in the Terminal Documentation](https://aider.chat/)
- [Roo Code / Cline - Autonomous Coding Agent Process Tree](https://github.com/RooVetGit/Roo-Code)

### C. Local Inference Servers & Tool Runtimes
- [Ollama - Windows Service & Background Process Architecture](https://ollama.com/)
- [LM Studio - Desktop & Headless CLI Runtime Documentation](https://lmstudio.ai/)
- [Model Context Protocol (MCP) Official Specification](https://modelcontextprotocol.io/)
- [Astral - uv & uvx Fast Ephemeral Tool Execution](https://docs.astral.sh/uv/)

### D. Architectural References & Prior Art
- [MR-Axel/monitor-sistema Architecture (Research Reference)](https://github.com/MR-Axel/monitor-sistema)
