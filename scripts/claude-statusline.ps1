param([switch]$Check)
# Called by Claude Code. Only allowlisted numeric usage fields are cached.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$bridgePath = Join-Path $projectRoot 'dist\ClaudeUsageBridge.exe'
try {
    if (-not (Test-Path -LiteralPath $bridgePath)) { throw 'Build Agent Usage Bar first.' }
    $buffer = New-Object char[] 2048
    $inputText = New-Object Text.StringBuilder
    while (($read = [Console]::In.Read($buffer, 0, $buffer.Length)) -gt 0) {
        if ($inputText.Length + $read -gt 65536) { throw 'Status-line input is too large.' }
        $null = $inputText.Append($buffer, 0, $read)
    }
    $raw = $inputText.ToString()
    if ($Check) { $raw | & $bridgePath --check }
    else { $raw | & $bridgePath }
} catch {
    [Console]::Out.WriteLine('Agent Usage Bar: Claude connection unavailable')
}
