# Maintainer playbook

The app uses C# 5, WinForms, and .NET Framework 4.8. There are no package dependencies.

From a Windows PowerShell prompt in the repository:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\scripts\test-local.ps1
```

For visual changes, regenerate demo assets with `scripts\render-docs-assets.ps1`
and inspect the actual 276 x 64 captures in light and dark mode. Check missing,
partial, stale, full, empty, and near-reset states. Never publish real account captures.
Before a commit, run `git diff --check` and inspect the exact staged files.

Claude setup is opt-in. Test it with `-SettingsPath` pointing to temporary
synthetic settings, never by overwriting a contributor's configuration.
The local test script uses fixtures, not provider requests.

The source-only publication has no CI runner, binary release, installer, or
deployment. New release automation needs separate approval and a reviewed
self-hosted execution boundary that excludes untrusted forks.

The internal `CodexUsageBar` namespace is retained for compatibility with the
existing renderer; the product and executable are called Agent Usage Bar.
