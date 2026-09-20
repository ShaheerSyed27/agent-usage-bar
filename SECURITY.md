# Security and trust

## Codex connection

`AgentUsageBar.cs` starts the installed `codex.exe app-server --stdio`, then sends
only `initialize`, `initialized`, and `account/rateLimits/read`. It also listens
for `account/rateLimits/updated`. It never starts a coding task, sends a model
turn, or redeems reset credits.

Codex uses its existing sign-in and contacts OpenAI for usage metadata. Its own
configuration, logs, and telemetry are controlled by Codex. The widget does not
read Codex credential files, conversations, or projects.

Discovery checks the Codex desktop installation, then explicit absolute local
PATH directories. Empty, relative, and network entries are skipped. No shell
or downloader is used to find Codex. Someone who can replace your installed
Codex executable or trusted PATH contents can replace what the widget starts.

## Claude connection

Claude Code's official status-line feature passes JSON to
`scripts/claude-statusline.ps1`, which invokes `ClaudeUsageBridge.exe`. The helper
accepts at most 64 KiB of JSON and writes only validated five-hour and seven-day
percentages, reset times, schema version, and receipt time. Other fields,
including session IDs, workspace paths, and transcript paths, are discarded.
It does not open transcript files, access login tokens, or call an OAuth endpoint.

Only the latest sample is kept, in `%LOCALAPPDATA%\AgentUsageBar\claude-usage.json`.
The bar checks this local file every two seconds and parses it only after a
change or a reset boundary. It does not request a model response or a fresh
remote quota reading. Old and absent data are labelled accordingly.

The opt-in setup script adds a status-line command to Claude's settings. It
backs up the previous file alongside that file, preserves unrelated settings,
and refuses to replace a different custom status line. Those local backups may
contain private settings: keep them local. Removal deletes only this project's
matching status-line command. The integration uses a documented feature:
[Claude Code status lines](https://code.claude.com/docs/en/statusline).

## Local changes and permissions

- Saves position, theme, always-on-top, and visibility for each bar in
  `%LOCALAPPDATA%\AgentUsageBar`. Can read earlier Codex Usage Bar preferences
  to preserve the existing Codex position and theme.
- Changes the current user's Windows startup entry only when startup is toggled.
- Writes a usage summary to the clipboard only when that menu action is chosen.
- Runs with normal user permissions. No elevation, listener, HTTP client,
  telemetry, tracking ID, remote updater, or maintainer endpoint.
- The installed provider tools have their own permissions and network behavior.
  This wrapper does not sandbox those tools. Keep their official installations current.

## Repository and build

The first publication is source-only. Review `build.ps1` before running it. It
uses the Windows .NET Framework compiler with no downloaded dependencies or
post-install hooks. Built executables and local configuration are excluded from Git.
Documentation images use sample values.

There are no automatic execution workflows. Never run untrusted fork code on a
personal self-hosted runner. Review contributions before building them locally.
The maintainer will never ask for a password, API key, session cookie, or remote
access to diagnose this utility.

There is no install count, active-user count, or user list. GitHub's own repository
traffic statistics are separate from the app.

This is a community project, not an independently audited security product.
Local checks cannot guarantee that software is free of bugs. Codex and Claude
protocol changes can affect compatibility. Security fixes target the latest
source on `main`.

## Report a security issue

Use [GitHub's private vulnerability report form](https://github.com/ShaheerSyed27/agent-usage-bar/security/advisories/new).
Send a minimal reproduction with synthetic data. Keep credentials, account IDs,
and private logs out of public issues.
