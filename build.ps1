$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$packageRoot = Join-Path $projectRoot 'dist'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "The Windows C# compiler was not found at $compiler"
}

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null

$source = Join-Path $projectRoot 'AgentUsageBar.cs'
$claudeData = Join-Path $projectRoot 'ClaudeUsageData.cs'
$claudeService = Join-Path $projectRoot 'ClaudeUsageService.cs'
$manifest = Join-Path $projectRoot 'app.manifest'
$executable = Join-Path $packageRoot 'AgentUsageBar.exe'
$appIcon = Join-Path $packageRoot 'AgentUsageBar.ico'
& (Join-Path $projectRoot 'scripts\create-app-icon.ps1') -OutputPath $appIcon

& $compiler `
    /nologo `
    /target:winexe `
    /platform:anycpu `
    /optimize+ `
    /langversion:5 `
    "/win32manifest:$manifest" `
    "/win32icon:$appIcon" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    "/out:$executable" `
    $source $claudeData $claudeService

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'README.txt') -Destination (Join-Path $packageRoot 'README.txt') -Force

$bridge = Join-Path $packageRoot 'ClaudeUsageBridge.exe'
& $compiler /nologo /target:exe /platform:anycpu /optimize+ /langversion:5 `
    /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll `
    "/out:$bridge" (Join-Path $projectRoot 'ClaudeUsageBridge.cs') $claudeData
if ($LASTEXITCODE -ne 0) { throw 'Claude bridge compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $packageRoot 'LICENSE') -Force

Write-Output "Built: $executable"
Write-Output "Claude bridge: $bridge"
