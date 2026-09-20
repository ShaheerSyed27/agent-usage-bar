AGENT USAGE BAR
===============

- Separate always-on-top Codex and Claude Code bars.
- Codex weekly percentage; Claude five-hour and weekly percentages.
- Tray percentages, light/dark mode, and local-time reset details.
- A small ember near the expiry of a Codex banked reset.
- No telemetry, credential collection, or model calls by this app.

BUILD AND RUN
-------------
From the source folder, review build.ps1, then run:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
Open dist\AgentUsageBar.exe.

For Codex, install and sign in to the official desktop app or CLI.
For Claude, run scripts\configure-claude.ps1 from the source folder after building.
Claude values arrive through its status line after a normal response. The widget
shows the age of the last sample; it cannot fetch fresh Claude data independently.

Drag to move. Right-click for options. F5 refreshes. Escape hides the focused bar.
Double-click a tray icon to show its bar. Quit closes both bars.
Start with Windows is optional and off by default.

Read README.md and SECURITY.md in the source folder for setup, privacy, and removal.
Windows 10/11 and .NET Framework 4.8 are required. No packages are downloaded.

SOURCE
------
https://github.com/ShaheerSyed27/agent-usage-bar
MIT licensed. Independent of OpenAI and Anthropic.
