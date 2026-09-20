param([switch]$Remove, [string]$SettingsPath)
$ErrorActionPreference = 'Stop'
if (-not $SettingsPath) {
    $configRoot = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
    $SettingsPath = Join-Path $configRoot 'settings.json'
}
$SettingsPath = [IO.Path]::GetFullPath($SettingsPath)
$scriptPath = (Join-Path $PSScriptRoot 'claude-statusline.ps1').Replace('\', '/')
# This path will be parsed by either PowerShell or Git Bash. Reject shell metacharacters.
if ($scriptPath -match '["`$\r\n]') { throw 'Move the project to a path without quotes, dollar signs, or backticks before connecting Claude.' }
$command = 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $scriptPath + '"'
$settings = [pscustomobject]@{}
$original = $null
if (Test-Path -LiteralPath $SettingsPath) {
    $original = [IO.File]::ReadAllText($SettingsPath)
    $settings = $original | ConvertFrom-Json
    if ($null -eq $settings -or $settings -is [array] -or $settings -isnot [pscustomobject]) { throw 'Expected a JSON settings object. No changes made.' }
}
$existing = $settings.PSObject.Properties['statusLine']
if ($Remove) {
    if (-not $existing) { Write-Output 'No status line is configured. Nothing changed.'; return }
    if ($existing.Value.command -cne $command) { throw 'This is not the Agent Usage Bar command for this folder. No changes made.' }
    $settings.PSObject.Properties.Remove('statusLine')
} else {
    if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\ClaudeUsageBridge.exe'))) { throw 'Run build.ps1 first.' }
    if ($existing -and $existing.Value.command -cne $command) { throw 'A custom Claude status line already exists. It was preserved. See docs\claude-setup.md to connect it manually.' }
    if ($existing) { Write-Output 'Agent Usage Bar is already connected. Nothing changed.'; return }
    $settings | Add-Member -NotePropertyName statusLine -NotePropertyValue ([pscustomobject]@{ type='command'; command=$command })
}
New-Item -ItemType Directory -Path (Split-Path -Parent $SettingsPath) -Force | Out-Null
if ($null -ne $original) {
    $backup = $SettingsPath + '.agent-usage-bar-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff') + '.bak'
    [IO.File]::WriteAllText($backup, $original, [Text.UTF8Encoding]::new($false))
}
$json = $settings | ConvertTo-Json -Depth 100
$temporary = $SettingsPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporary, $json, [Text.UTF8Encoding]::new($false))
    # Refuse to overwrite settings if another program changed them during setup.
    if ($null -ne $original) {
        if (-not (Test-Path -LiteralPath $SettingsPath) -or [IO.File]::ReadAllText($SettingsPath) -cne $original) { throw 'Claude settings changed during setup. Retry; no settings were overwritten.' }
        [IO.File]::Replace($temporary, $SettingsPath, [NullString]::Value)
    } else { [IO.File]::Move($temporary, $SettingsPath) }
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
Write-Output $(if ($Remove) { 'Claude bridge removed. Other settings were preserved.' } else { 'Claude connected. Use Claude Code normally; usage appears after it reports subscription limits.' })
