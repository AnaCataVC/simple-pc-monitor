> **Created:** 2026-09-20
> **Last Updated:** 2026-09-20

# AI Session Transcript Maintenance, Storage Footprint & Safe Pruning Architecture

## 1. Overview & Problem Definition

Modern AI development workflows with CLI agents (Anthropic Claude Code, Google Gemini CLI, Google Antigravity, OpenAI Codex, Aider) generate substantial telemetry on developer workstations. To maintain conversational state, multi-turn reasoning, and subagent orchestration, these tools employ **append-only JSONL (JSON Lines)** logging and per-session database engines.

Because individual sessions capture complete tool call payloads (including large file diffs, command-line outputs, HTML/SVG artifacts, and subagent thinking trees), individual transcript files rapidly scale between **1 MB and 25 MB each**. Over months of active development across hundreds of repositories and feature branches, AI session folders accumulate **gigabytes of unpruned disk bloat** that standard operating system cleanup tools (such as Windows Disk Cleanup) deliberately skip.

This document formalizes the internal filesystem layout, privacy constraints, age-guard invariants, and safe maintenance patterns for **System Core Monitor**'s storage optimization engine.

---

## 2. Local Workstation Footprint (Observed Data)

- **`%USERPROFILE%\.claude\projects\`**: 115 distinct project directories. Session `.jsonl` files range from **150 KB to >12.4 MB each**, alongside companion directories housing `subagents/` (with `agent-*.jsonl` and `.meta.json`) and `tool-results/` (execution outputs up to 680 KB each).
- **`%USERPROFILE%\.gemini\antigravity\brain\`**: 513 conversation UUID folders. Each folder: `.system_generated\logs\` holds `transcript.jsonl`, `transcript_full.jsonl`, and chunked logs.
- **`%USERPROFILE%\.gemini\antigravity-cli\conversations\`**: 88 SQLite `.db` databases, ranging up to 6.8 MB each.

---

## 3. Directory Structure & Transcript Locations Matrix

### A. Comparison: Claude CLI vs. Gemini CLI / Antigravity

| Dimension | Anthropic Claude Code (CLI) | Google Gemini CLI / Antigravity | Claude Desktop App |
| :--- | :--- | :--- | :--- |
| **Primary Base Path** | `%USERPROFILE%\.claude\projects\` | `%USERPROFILE%\.gemini\` | `%LOCALAPPDATA%\Claude\` |
| **Project Grouping** | Encoded directory slug (`C--Users-...`) | Dedicated UUIDs in `brain\` / `history\` | Per-session folders |
| **Active Transcripts** | `<session-uuid>.jsonl` | `brain\<uuid>\.system_generated\logs\transcript.jsonl` | `local-agent-mode-sessions\` |
| **Subagent Storage** | `<session-uuid>\subagents\agent-*.jsonl` | `brain\<uuid>\.system_generated\steps\` | Internal process logs |
| **Tool Artifacts** | `<session-uuid>\tool-results\` | `brain\<uuid>\.system_generated\logs\chunks\` | Embedded in session |
| **CLI Conversation DB** | N/A (Pure JSONL) | `antigravity-cli\conversations\<uuid>.db` | Local SQLite databases |
| **Persistent Context** | `CLAUDE.md`, `memory\` subfolder | `GEMINI.md`, `knowledge\` subfolder | App preferences / settings |

---

## 4. Filesystem Hierarchy Breakdown

### A. Anthropic Claude Code (`%USERPROFILE%\.claude\projects\`)

Claude Code isolates transcripts per workspace using a slugified path convention where slashes and drive colons are converted into hyphen sequences:

```text
%USERPROFILE%\.claude\
├── CLAUDE.md                           # Global developer rules (CRITICAL - DO NOT TOUCH)
├── history.jsonl                       # Global CLI command invocation history
├── settings.json                       # Tool configuration
└── projects\
    ├── <encoded-workspace-path-A>\
    │   ├── memory\                     # Persistent project memory (CRITICAL - DO NOT TOUCH)
    │   ├── <session-uuid-1>.jsonl      # Root session transcript (1 MB - 25 MB)
    │   ├── <session-uuid-1>\           # Companion folder for session 1
    │   │   ├── subagents\
    │   │   │   ├── agent-<id>.jsonl    # Autonomous subagent transcript
    │   │   │   └── agent-<id>.meta.json
    │   │   └── tool-results\           # Bloat artifacts: command stdout, HTML, MCP dumps
    │   │       ├── artifact-<guid>.html
    │   │       └── mcp-<server>-<ts>.txt
    │   └── <session-uuid-2>.jsonl
    └── <encoded-workspace-path-B>\
```

### B. Google Gemini CLI & Antigravity (`%USERPROFILE%\.gemini\`)

```text
%USERPROFILE%\.gemini\
├── GEMINI.md                           # Global prompt instructions (CRITICAL - DO NOT TOUCH)
├── antigravity\
│   └── brain\
│       └── <conversation-uuid>\
│           ├── scratch\                # Ephemeral test scripts and scratchpads
│           └── .system_generated\
│               ├── steps\              # Per-step tool execution envelopes
│               └── logs\
│                   ├── transcript.jsonl
│                   ├── transcript_full.jsonl
│                   └── chunks\transcript\
└── antigravity-cli\
    ├── conversations\
    │   └── <session-uuid>.db           # SQLite state databases (200 KB - 10 MB)
    └── history.jsonl
```

---

## 5. Privacy-Preserving Scanning Invariants (Zero Content Inspection)

AI session transcripts record everything an engineer or AI touches:
- Uncommitted source code and architectural models.
- Environment variables, database connection strings, and API keys output in CLI logs.
- Proprietary customer data processed during local testing.

### Mandatory Scanning Rules:
1. **Zero Payload Reading**: The scanner must **NEVER** open `.jsonl`, `.txt`, `.html`, or `.db` files with `File.OpenRead()`, `File.ReadAllText()`, or any stream parser.
2. **NTFS Metadata Only**: All telemetry calculations rely exclusively on Win32 directory table metadata via `System.IO.FileInfo` and `System.IO.DirectoryInfo`:
   - `FileInfo.Length`
   - `FileInfo.CreationTimeUtc`
   - `FileInfo.LastWriteTimeUtc`
   - `FileInfo.Attributes`
3. **No External Logging of Session Titles**: The UI displays aggregated metrics per workspace (e.g., *45 sessions, 142 MB*), never echoing prompt excerpts or conversation topics.

---

## 6. Age Guard Architecture & Dual Timestamp Invariant

### A. The 24-Hour Guard Rationale
Session transcripts must never be cleaned while an active task, background build, or overnight run might still resume.
- AI CLI sessions frequently persist across workstation sleep cycles or multi-day branches.
- Deleting an active session transcript destroys the agent's ability to resume and corrupts in-flight context.
- **Hard Rule**: The engine must enforce an absolute minimum age floor of **24 hours** (`Math.Max(24, retentionHours)`). Even with aggressive settings, files newer than 24 hours are immutable.

### B. Dual Timestamp Validation Guard
Relying solely on `LastWriteTimeUtc` is dangerous because modification stamps can be reset during archive extractions or file copies.

**Enforced Invariant:**
```csharp
// Both timestamps must be strictly older than the cutoff threshold
if (file.LastWriteTimeUtc < cutoffUtc && file.CreationTimeUtc < cutoffUtc)
{
    // Candidate eligible for safe pruning
}
```

---

## 7. Retention Design & Subagent Subfolder Pruning

### A. Configurable Retention Policies
Session utility decays non-linearly: 95% of resumed sessions occur within 48 hours. After 7 days, transcripts are inert disk bloat.

| Policy | Default | Notes |
| :--- | :--- | :--- |
| Aggressive (CI / constrained disk) | 24 hours | Minimum floor |
| **Default (standard developer)** | **7 days** | Persisted in `config.json` |
| Conservative (long-horizon auditing) | 30 days | Optional UI setting |

### B. Atomic Unit & Protected Root List

A session consists of:
1. Root transcript: `<session-uuid>.jsonl`
2. Companion directory: `<session-uuid>\` (containing `subagents\`, `tool-results\`, `.meta.json`)

**Structural Invariants:**
1. **Atomic Lifecycle**: A subagent log must never be deleted if its parent session is active (< cutoff).
2. **Cascade Cleanup**: When `<session-uuid>.jsonl` exceeds the cutoff, the entire companion `<session-uuid>\` directory is deleted as a single transaction.
3. **Reparse Point Guard**: Every directory traversal must call `FileSystemSafety.IsReparsePoint()`.
4. **Persistent Root Blacklist**: Directories and files named `memory\`, `knowledge\`, `settings.json`, `CLAUDE.md`, `GEMINI.md` are permanently blacklisted from deletion.

---

## 8. Safe Concurrency & Process Lock Handling

When an AI CLI is currently active, Windows places a share lock on the active transcript file.

### Resilient Deletion Strategy:
```csharp
try
{
    if (!FileSystemSafety.IsReparsePoint(file))
    {
        file.Attributes = FileAttributes.Normal;
    }
    file.Delete();
    result.BytesFreed += fileLength;
    result.FilesDeleted++;
}
catch (IOException) // Win32 ERROR_SHARING_VIOLATION (0x80070020)
{
    result.LockedCount++; // File held by active agent; skip silently
}
catch (UnauthorizedAccessException)
{
    result.LockedCount++;
}
```

**Rules:**
- **Zero Process Disruption**: Never attempt kernel handle dropping to force a delete. If locked, skip until the next maintenance cycle.
- **Empty Directory Pruning**: Only prune `<session-uuid>\` after all child files are successfully deleted (`Directory.EnumerateFileSystemEntries().Any() == false`).

---

## 9. Implementation Blueprint for System Core Monitor

### New File: `Core/AiTranscriptCleaner.cs`
- `Scan(int retentionDays = 7, CancellationToken ct)` → returns `AiTranscriptAnalysisResult` (bytes, file count, eligible, workspace breakdown).
- `Prune(int retentionDays = 7, IProgress<T> progress, CancellationToken ct)` → performs atomic cascade deletion.
- Enforces the dual-timestamp 24-hour guard.
- Integrates with `FileSystemSafety.IsReparsePoint()`.

### Config Integration: `AppConfig`
- New property: `TranscriptRetentionDays` (default: 7).
- Persisted in `%APPDATA%\SystemCoreMonitor\config.json`.

### UI Target: `AiAgentsView` (post-MVVM refactor)
- Metric card: **"AI Transcripts"** — total size, session count, recoverable space.
- Button: **"Clean Inactive Transcripts"** → confirmation dialog with preview list.
- Slider/NumericUpDown: retention period (days).
