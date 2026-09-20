# Connect Claude Code

This uses Claude Code's [documented status-line data](https://code.claude.com/docs/en/statusline).
It does not read `.credentials.json`, reuse an OAuth token, or call a model.

## Automatic setup

Build the app, then run this from the source folder:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\configure-claude.ps1
```

The script targets `settings.json` in `CLAUDE_CONFIG_DIR`, if set, or your
`.claude` folder otherwise. It creates a local backup before changing anything
and adds a small status line showing your remaining limits.

Use Claude Code normally after setup. Pro and Max subscription windows arrive
after Claude Code has received a response. Each window can be absent, including
after its reset time. An absent limit stays unavailable; it is not guessed to be
100%. The UI uses the five-hour window in the ring when present and shows the
weekly allowance beneath it. With only weekly data, the ring shows weekly usage.

The last reading stays visible while Claude Code is idle or closed. After three
minutes the tray and status dot signal an older sample. Hover for the exact
receipt time. Refresh only rereads the local sample.

Status-line rendering itself consumes no API tokens. Your ordinary Claude Code
conversations still consume their usual allowance. API billing, gateway spend,
context-window usage, and per-model quota windows are outside this integration.

## If you already have a custom status line

Automatic setup leaves it untouched. For a PowerShell status-line script, pass
the original JSON to the compiled bridge alongside your existing renderer:

```powershell
$raw = [Console]::In.ReadToEnd()
# Your existing renderer should use $raw instead of reading stdin a second time.
$raw | & 'C:\path\to\Agent Usage Bar\dist\ClaudeUsageBridge.exe' | Out-Null
# Continue your own rendering here.
```

Use your actual installation path. The bridge writes the limited local sample
and outputs a short summary; piping to `Out-Null` preserves your current visual
status line. Do not save the raw JSON or log it.

Project-level Claude settings can override user settings. Check those if the
widget is waiting for data even after using Claude. With WSL, the helper must
run on Windows and feed the Windows cache; automatic WSL setup is not included.

## Disconnect

From the same folder used to connect:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\configure-claude.ps1 -Remove
```

This removes only the matching command. If you integrated it into your own script,
remove the bridge invocation there instead. Disconnect before moving or deleting
the project folder, then connect again from its new location if needed.
