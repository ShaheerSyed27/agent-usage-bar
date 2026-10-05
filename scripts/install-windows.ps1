param([switch]$Startup,[switch]$ClaudeDesktop,[switch]$NoLaunch,[switch]$DryRun)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildRoot = Join-Path $projectRoot 'dist'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\Agent Usage Bar'
$executable = Join-Path $installRoot 'AgentUsageBar.exe'
$programs = [Environment]::GetFolderPath('Programs')
$desktop = [Environment]::GetFolderPath('Desktop')
$configRoot = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
$pluginRoot = Join-Path $configRoot 'skills\agent-usage-bar'
$marker = Join-Path $installRoot 'AgentUsageBar.install'
$markerText = 'Agent Usage Bar local installation v1'
$files = @('AgentUsageBar.exe','ClaudeUsageBridge.exe','AgentUsageBar.ico','README.txt','LICENSE')
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath (Join-Path $buildRoot $file))) { throw 'Run build.ps1 first.' }
}
if ((Test-Path -LiteralPath $installRoot) -and @(Get-ChildItem -LiteralPath $installRoot -Force).Count -gt 0 -and
    (-not (Test-Path -LiteralPath $marker) -or [IO.File]::ReadAllText($marker) -cne $markerText)) {
    throw 'The installation folder contains unrelated or unrecognized files. Nothing was overwritten.'
}
$links = @(
    (Join-Path $programs 'Agent Usage Bar (Codex + Claude).lnk'),
    (Join-Path $desktop 'Agent Usage Bar.lnk')
)
$shell = New-Object -ComObject WScript.Shell
foreach ($linkPath in $links) {
    if (Test-Path -LiteralPath $linkPath) {
        $existing = $shell.CreateShortcut($linkPath)
        $knownTargets = @($executable,(Join-Path $buildRoot 'AgentUsageBar.exe'),
            (Join-Path (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Web development projects\Agent Usage Bar\dist') 'AgentUsageBar.exe'))
        if ($existing.TargetPath -notin $knownTargets -or $existing.Arguments) {
            throw "The existing shortcut has a different target and was preserved: $linkPath"
        }
    }
}
if ($ClaudeDesktop -and (Test-Path -LiteralPath $pluginRoot)) {
    $manifestPath = Join-Path $pluginRoot '.claude-plugin\plugin.json'
    if (-not (Test-Path -LiteralPath $manifestPath) -or
        ([IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json).name -ne 'agent-usage-bar') {
        throw 'An unrelated Claude plugin already occupies the target folder. It was preserved.'
    }
}
if ($DryRun) {
    [pscustomobject]@{InstallDirectory=$installRoot;StartMenuShortcut=$links[0];DesktopShortcut=$links[1];Startup=[bool]$Startup;ClaudeDesktop=[bool]$ClaudeDesktop} 
    return
}
# Updating a running copy can corrupt its install. Leave it untouched and ask to quit.
foreach ($file in @('AgentUsageBar.exe','ClaudeUsageBridge.exe')) {
    $destination = Join-Path $installRoot $file
    if (Test-Path -LiteralPath $destination) {
        try { $probe = [IO.File]::Open($destination,'Open','ReadWrite','None'); $probe.Dispose() }
        catch { throw 'Quit Agent Usage Bar from its tray menu before updating the installation.' }
    }
}
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
[IO.File]::WriteAllText($marker,$markerText,[Text.UTF8Encoding]::new($false))
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $buildRoot $file) -Destination (Join-Path $installRoot $file) -Force }
foreach ($linkPath in $links) {
    $link = $shell.CreateShortcut($linkPath)
    $link.TargetPath = $executable
    $link.WorkingDirectory = $installRoot
    $link.IconLocation = (Join-Path $installRoot 'AgentUsageBar.ico') + ',0'
    $link.Description = 'Codex and Claude Code usage percentages'
    $link.Save()
}
if ($Startup) {
    $runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    New-Item -Path $runPath -Force | Out-Null
    New-ItemProperty -Path $runPath -Name AgentUsageBar -PropertyType String -Value ('"' + $executable + '" --startup') -Force | Out-Null
    # Remove only the exact earlier widget entry, not an unrelated similarly named program.
    $legacy = (Get-ItemProperty -Path $runPath -Name CodexUsageBar -ErrorAction SilentlyContinue).CodexUsageBar
    $oldExe = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Web development projects\Codex Usage Bar\dist\CodexUsageBar.exe'
    if ($legacy -eq ('"' + $oldExe + '"')) { Remove-ItemProperty -Path $runPath -Name CodexUsageBar }
}
if ($ClaudeDesktop) {
    $sourceRoot = Join-Path $projectRoot 'claude-plugin'
    foreach ($relative in @('.claude-plugin\plugin.json','hooks\hooks.json','hooks\register.js')) {
        $destination = Join-Path $pluginRoot $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceRoot $relative) -Destination $destination -Force
    }
    Write-Output 'Claude desktop bridge installed. Use desktop Code 2.1.287+ with mods enabled, then open a new local Code session or restart an idle one. No prompt was sent.'
}
Write-Output 'Installed for this Windows user. Search Start for Agent Usage Bar, Codex, or Claude.'
Write-Output 'To pin it: right-click the Start search result and choose Pin to Start.'
if (-not $NoLaunch) { Start-Process -FilePath $executable -WorkingDirectory $installRoot -WindowStyle Hidden }
