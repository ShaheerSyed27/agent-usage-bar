# Initial publication checks

Checked on Windows on 21 September 2026 for Agent Usage Bar 2.0.0.

- Both executables compile with the Windows .NET Framework compiler, without package downloads.
- `scripts/test-local.ps1`: 20 focused checks pass. These cover the compiled
  bridge and PowerShell wrapper, private-field filtering, percentage conversion,
  absent and expired limits, malformed values, future timestamps, executable
  discovery, configuration backups, idempotent setup, safe removal, custom
  status-line preservation, and provider-specific primary windows.
- Gitleaks 8.30.1 reports no leaks in the project. An additional local scan found
  no personal paths, private email, or credential-pattern matches in publication files.
- Inspected the real renderer at 276 x 64 for Codex and Claude in light and dark
  mode, the 1280 x 640 overview, and the local-time expiry screenshot. The
  760 x 280 GIF contains 32 frames with a 100 ms delay and an infinite loop.
- The application starts locally as one process. Its Claude connection waits
  for a normal status-line report; no model request was generated for testing.
- The public file list contains source, scripts, documentation, and demo images.
  It excludes binaries, local settings, usage caches, logs, and credentials.

Claude's documented schema is tested with fixtures. A fresh live Claude account
response, WSL setup, other subscription plans, screen-reader behavior, and
every Windows display-scaling combination have not been independently verified.
The screenshots are examples, not evidence of a live Claude account connection.

These checks are not an independent security audit or a promise of zero bugs.
