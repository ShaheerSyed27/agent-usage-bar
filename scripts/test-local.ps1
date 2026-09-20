$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$projectRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $projectRoot 'dist\AgentUsageBar.exe'))
$bridgePath = Join-Path $projectRoot 'dist\ClaudeUsageBridge.exe'
$static = [Reflection.BindingFlags]'Static, NonPublic, Public'
$instance = [Reflection.BindingFlags]'Instance, NonPublic, Public'
$passed = 0
function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw "FAIL: $message" }
    $script:passed++
    Write-Output "PASS: $message"
}
function Parse-Claude([string]$json, [DateTime]$now) {
    $type = $assembly.GetType('CodexUsageBar.ClaudeUsageService', $true)
    return $type.GetMethod('ParseCache', $static).Invoke($null, @($json, $now))
}
$now = [DateTime]::UtcNow
$epoch = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$payload = @{
    session_id='PRIVATE_SAMPLE_DO_NOT_PERSIST'; transcript_path='PRIVATE_SAMPLE_DO_NOT_PERSIST';
    cwd='PRIVATE_SAMPLE_DO_NOT_PERSIST'; model=@{display_name='PRIVATE_SAMPLE_DO_NOT_PERSIST'};
    rate_limits=@{
        five_hour=@{used_percentage=34.5; resets_at=$epoch+3600; ignored='PRIVATE_SAMPLE_DO_NOT_PERSIST'};
        seven_day=@{used_percentage=18; resets_at=$epoch+172800};
        spend_limit=@{used_percentage=10; resets_at=$epoch+3000}
    }
} | ConvertTo-Json -Depth 8 -Compress
$process = New-Object Diagnostics.Process
$process.StartInfo = New-Object Diagnostics.ProcessStartInfo -Property @{
    FileName=$bridgePath; Arguments='--check'; UseShellExecute=$false;
    RedirectStandardInput=$true; RedirectStandardOutput=$true; RedirectStandardError=$true; CreateNoWindow=$true
}
try {
    $null = $process.Start()
    $process.StandardInput.WriteLine($payload)
    $process.StandardInput.Close()
    $sanitized = $process.StandardOutput.ReadToEnd()
    if (-not $process.WaitForExit(10000)) { $process.Kill(); throw 'Bridge did not finish.' }
    Assert ($process.ExitCode -eq 0) 'Compiled Claude bridge accepts documented status-line JSON'
} finally { $process.Dispose() }
$cache = $sanitized | ConvertFrom-Json
$wrapper = New-Object Diagnostics.Process
$wrapper.StartInfo = New-Object Diagnostics.ProcessStartInfo -Property @{
    FileName=(Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe');
    Arguments=('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + (Join-Path $PSScriptRoot 'claude-statusline.ps1') + '" -Check');
    UseShellExecute=$false; RedirectStandardInput=$true; RedirectStandardOutput=$true; RedirectStandardError=$true; CreateNoWindow=$true
}
try {
    $null = $wrapper.Start()
    $wrapper.StandardInput.WriteLine($payload)
    $wrapper.StandardInput.Close()
    $wrappedOutput = $wrapper.StandardOutput.ReadToEnd()
    if (-not $wrapper.WaitForExit(10000)) { $wrapper.Kill(); throw 'Status-line wrapper did not finish.' }
    $wrapped = $wrappedOutput | ConvertFrom-Json
    Assert ($wrapped.five_hour.used_percentage -eq 34.5 -and -not $wrappedOutput.Contains('PRIVATE_SAMPLE_DO_NOT_PERSIST')) 'PowerShell status-line wrapper forwards stdin and preserves the privacy filter'
} finally { $wrapper.Dispose() }
Assert (-not $sanitized.Contains('PRIVATE_SAMPLE_DO_NOT_PERSIST')) 'Session IDs, transcript paths, project names, and arbitrary fields are discarded'
Assert (@($cache.PSObject.Properties.Name).Count -eq 4 -and -not $cache.PSObject.Properties['spend_limit']) 'Cache contains only schema, timestamp, and the two supported usage windows'
    $sample = Parse-Claude $sanitized $now
Assert ($sample.SessionWindow.RemainingPercent -eq 65.5 -and $sample.WeeklyWindow.RemainingPercent -eq 82) 'Used percentages become the correct remaining percentages'
$afterReset = Parse-Claude $sanitized $now.AddHours(2)
Assert ($null -eq $afterReset.SessionWindow -and $null -ne $afterReset.WeeklyWindow) 'An expired Claude window becomes unavailable, never a guessed 100 percent'
$dataType = $assembly.GetType('CodexUsageBar.ClaudeUsageData', $true)
$sanitize = $dataType.GetMethod('Sanitize', $static)
$empty = $sanitize.Invoke($null, @('{"model":{"display_name":"sample"}}', $now))
Assert ($empty.Count -eq 2) 'Missing rate limits remain missing'
$bad = '{"rate_limits":{"five_hour":{"used_percentage":101,"resets_at":' + ($epoch+3600) + '},"seven_day":{"used_percentage":null,"resets_at":' + ($epoch+3600) + '}}}'
$invalid = $sanitize.Invoke($null, @($bad, $now))
Assert ($invalid.Count -eq 2) 'Null and out-of-range percentages are rejected'
$invalidCache = '{"schema_version":1,"captured_at":' + ($epoch+3600) + '}'
Assert ($null -eq (Parse-Claude $invalidCache $now)) 'Future cache timestamps are rejected'

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('agent-usage-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $fakeExe = Join-Path $temporaryRoot 'codex.exe'
    [IO.File]::WriteAllText($fakeExe, 'test fixture, never executed')
    $finder = $assembly.GetType('CodexUsageBar.CodexUsageService').GetMethod('FindCodexOnPath', $static)
    Assert ($null -eq $finder.Invoke($null, @('.;relative;C:relative;\\server\share;;'))) 'Codex discovery rejects relative, empty, and network PATH entries'
    Assert ($finder.Invoke($null, @([string]$temporaryRoot)) -eq $fakeExe) 'Codex discovery accepts an explicit local installation directory'
    $settingsPath = Join-Path $temporaryRoot 'settings.json'
    $original = '{"permissions":{"allow":["Read"]},"theme":"dark"}'
    [IO.File]::WriteAllText($settingsPath, $original)
    & (Join-Path $PSScriptRoot 'configure-claude.ps1') -SettingsPath $settingsPath | Out-Null
    $configured = [IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json
    Assert ($configured.permissions.allow[0] -eq 'Read' -and $configured.theme -eq 'dark' -and $configured.statusLine.type -eq 'command') 'Claude setup preserves unrelated settings'
    $backups = @(Get-ChildItem -LiteralPath $temporaryRoot -Filter '*.bak')
    Assert ($backups.Count -eq 1 -and [IO.File]::ReadAllText($backups[0].FullName) -ceq $original) 'Claude setup creates an exact local backup'
    & (Join-Path $PSScriptRoot 'configure-claude.ps1') -SettingsPath $settingsPath | Out-Null
    Assert (@(Get-ChildItem -LiteralPath $temporaryRoot -Filter '*.bak').Count -eq 1) 'Repeated setup is idempotent'
    & (Join-Path $PSScriptRoot 'configure-claude.ps1') -SettingsPath $settingsPath -Remove | Out-Null
    $removed = [IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json
    Assert (-not $removed.PSObject.Properties['statusLine'] -and $removed.theme -eq 'dark') 'Removal leaves other Claude settings intact'
    $custom = '{"statusLine":{"type":"command","command":"my-existing-status-line"},"theme":"dark"}'
    [IO.File]::WriteAllText($settingsPath, $custom)
    $refused = $false
    try { & (Join-Path $PSScriptRoot 'configure-claude.ps1') -SettingsPath $settingsPath | Out-Null } catch { $refused = $true }
    Assert ($refused -and [IO.File]::ReadAllText($settingsPath) -ceq $custom) 'An existing custom status line is never replaced'
    $formType = $assembly.GetType('CodexUsageBar.UsageBarForm', $true)
    $providerType = $assembly.GetType('CodexUsageBar.UsageProvider', $true)
    foreach ($name in @('Codex','Claude')) {
        $provider = [Enum]::Parse($providerType, $name)
        $form = [Activator]::CreateInstance($formType, @($provider))
        try {
            Assert ($form.Text.StartsWith($name) -and $form.ClientSize.Width -eq 276 -and $form.ClientSize.Height -eq 64) "$name has a correctly labelled compact window"
            $formType.GetField('_snapshot', $instance).SetValue($form, $sample)
            $selected = $formType.GetProperty('DisplayWindow', $instance).GetValue($form, $null)
            Assert ($selected.DurationMinutes -eq $(if ($name -eq 'Claude') {300} else {10080})) "$name uses the correct primary limit"
            $bitmap = New-Object Drawing.Bitmap 276,64
            try { $form.DrawToBitmap($bitmap, [Drawing.Rectangle]::new(0,0,276,64)) } finally { $bitmap.Dispose() }
        } finally { $form.Dispose() }
    }
} finally {
    # The target is the exact unique test folder created above, never a project or home folder.
    $resolved = [IO.Path]::GetFullPath($temporaryRoot)
    $expectedRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolved.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved).StartsWith('agent-usage-tests-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
Write-Output "$passed focused checks passed. No account request or model turn was made."
