# Contributing

Small improvements and clear bug reports are welcome.

1. Fork the repository and make a branch for your change.
2. Build with `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`.
3. Run `powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\scripts\test-local.ps1`.
4. For UI changes, check light and dark mode, the tray, missing or stale data,
   and a near-expiry reset using sample data. The demo renderer is
   `scripts\render-docs-assets.ps1`.
5. Open a pull request with what changed and how you checked it.

Keep the app small. No telemetry, auto-updater, credential collection, or
reset-redemption action. New dependencies need a clear reason.

Never commit built executables, personal screenshots, usage logs, credentials,
or machine-specific settings. Security reports belong in the private reporting
channel linked from `SECURITY.md`.

Checks run locally. There are no GitHub Actions workflows, and untrusted fork
code must never be executed on a maintainer's personal runner.
