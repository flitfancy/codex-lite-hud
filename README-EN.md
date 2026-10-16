# Codex Lite HUD

**Keep the current chat's usage beside the Codex sidebar.**

[简体中文](README.md) · [First run](first-run.md) · [Changelog](CHANGELOG.md) · [Releases](https://github.com/flitfancy/codex-lite-hud/releases)

Codex Lite HUD is a small Windows overlay showing the selected local chat's cumulative input and output tokens, recorded context usage, request-average tokens per second, and remaining account quota. Current version: **1.0.1**.

Download the Windows x64 release ZIP, extract it into a writable folder, and run `CodexLiteHud.exe`. Magpie, Node.js and Python are not required. The tested environment is Windows 11 x64 with .NET Framework 4.8-series and the Windows Codex desktop app. Other Windows/client versions, CLI sessions and remote-only chats require separate validation.

## Readings and behavior

- Input/output each show the latest recorded request (本次) and chat total (对话). Input includes cached tokens; output includes reasoning tokens. A user turn can contain several requests; the latest request is not a turn total.
- Context is the latest recorded request's input plus output, divided by the recorded context window, rather than cumulative chat input.
- Speed includes request waiting time and is not a pure decoding benchmark.
- Nine blocks are shown by default; model is hidden and available from the menu. Turns/compactions follow context, then cache share. Request duration precedes runtime/span; runtime and span use separate larger readings. Right-click to toggle or reorder blocks. Choices persist, with a restore-default action. The HUD and menus have no hover tooltips.
- Account quota comes from local log snapshots. Each recorded window shows its duration and remaining percentage, such as 5 hours and 7 days. One window takes one line; a second line appears only when a second valid window exists. Expired windows release their line.
- Turns use distinct Codex `turn_id` start events. Compactions count persisted top-level `compacted` records, deduplicated by available IDs. Tool calls, in-turn steering and embedded replacement history do not create extra turns; missing turn events display an unknown value.
- Time has two separate readings: cumulative turn runtime and elapsed span since the first retained record. Runtime includes tool and interaction waiting; active turns keep counting. The CLH header stays at the top, while the chat name expands and contracts with its wrapped text.
- The overlay follows the selected local chat and the main window. It stays with a visible Codex window when another app is active, follows normal window stacking, and hides when Codex is hidden, minimized or closed.
- The HUD starts locked to the sidebar with a normal pointer. Right-click to unlock two-axis dragging, then lock it back to the sidebar. The free position is saved relative to the main window. Pet/tool windows are excluded from target selection.
- Quota watches log changes across local chats, checked roughly every 200 ms, with a one-second regular data refresh. Faster quota reads do not increase UI Automation scanning; server snapshots may still arrive late.

## Local data

The installer supports `-WithCodex` to create a combined `Codex + HUD` desktop shortcut. Its short-lived launcher uses the installed app identity and adds no resident watcher. Installing shortcuts does not start the HUD; open the executable or shortcut afterward.

The default log directory is `%USERPROFILE%\.codex`; `CODEX_HOME` is supported. Current names are merged from the local index and read-only state database metadata, including new chats and renames not yet reflected in the index. Portable releases store settings in adjacent `data/`, controlled by `portable.flag`. Without the marker, settings use `%LOCALAPPDATA%\CodexLiteHud`; `CODEX_LITE_HUD_DATA_HOME` overrides that location.

Quota compatibility has generated-record checks for Free, Plus and Pro. Window values come from recorded snapshots, missing quota stays unknown, and malformed windows do not interrupt other statistics. Live Free/Plus account acceptance is still pending. See [data details](docs/DATA.md) for format and coverage limits.

The HUD makes no network or model requests, reads no authentication keys, and does not modify Codex logs, configuration or databases. Its local diagnostics may include chat names and token counts. Codex's internal UI and data formats may change; ambiguous selection requires manual pinning. See [data details](docs/DATA.md).

## Development

Use Windows and the installed .NET Framework compiler:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\release.ps1 -DryRun
```

The release uses an explicit public file allowlist and a SHA-256 checksum. Local state, tests, backups and build intermediates stay outside the distribution. See [development](docs/DEVELOPMENT.md) and [performance scope](docs/PERFORMANCE.md).

[MIT](LICENSE) · Copyright (c) 2026 FlitFancy.
