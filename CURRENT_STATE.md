# Current state

- Agent Usage Bar 2.0.0, Windows 10/11, .NET Framework 4.8.
- One process owns two independently movable 276 x 64 bars and two tray icons.
- Codex reads weekly account metadata through the installed app-server.
- Claude displays five-hour and weekly subscription data from the optional
  official status-line bridge. It does not independently fetch usage.
- Missing or expired Claude windows stay unavailable; older samples show their age.
- Only Codex banked-reset expiry activates the ember. It respects Windows UI effects.
- Local build and fixture tests are available. No automated workflows or telemetry.
- Build and Windows rendering verified locally. Claude's documented input schema,
  bridge filtering, setup, and missing-data behavior are fixture-tested; a fresh
  live Claude account response has not been generated specifically for validation.

See `README.md` for user behavior and `SECURITY.md` for the trust boundaries.
