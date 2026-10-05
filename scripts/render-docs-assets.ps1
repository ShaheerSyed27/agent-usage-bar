param([string]$PythonPath = 'python', [switch]$SkipAnimation, [string]$OutputPath)
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$renderRoot = Join-Path $projectRoot 'dist\documentation'
$assemblyPath = Join-Path $renderRoot 'DocumentationRenderer.dll'
$outputRoot = if ($OutputPath) { [IO.Path]::GetFullPath($OutputPath) } else { Join-Path $projectRoot 'docs\screenshots' }
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
New-Item -ItemType Directory -Force -Path $renderRoot | Out-Null

# Compile the actual app drawing code with a DPI-aware GDI adapter. This is a
# documentation-only library, never shipped or loaded by the running widget.
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:library /define:DOCUMENTATION_RENDER /optimize+ /langversion:5 `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll `
    "/out:$assemblyPath" (Join-Path $projectRoot 'AgentUsageBar.cs') `
    (Join-Path $projectRoot 'ClaudeUsageData.cs') (Join-Path $projectRoot 'ClaudeUsageService.cs') `
    (Join-Path $PSScriptRoot 'DocumentationRenderer.cs')
if ($LASTEXITCODE -ne 0) { throw 'Documentation renderer compilation failed.' }

$assembly = [Reflection.Assembly]::LoadFile($assemblyPath)
$formType = $assembly.GetType('CodexUsageBar.UsageBarForm', $true)
$snapshotType = $assembly.GetType('CodexUsageBar.UsageSnapshot', $true)
$windowType = $assembly.GetType('CodexUsageBar.UsageWindow', $true)
$creditType = $assembly.GetType('CodexUsageBar.ResetCredit', $true)
$creditListType = [Collections.Generic.List``1].MakeGenericType($creditType)
$snapshotField = $formType.GetField('_snapshot', [Reflection.BindingFlags]'Instance, NonPublic')
$darkModeField = $formType.GetField('_darkMode', [Reflection.BindingFlags]'Instance, NonPublic')
$phaseField = $formType.GetField('_attentionPhase', [Reflection.BindingFlags]'Instance, NonPublic')
$serviceErrorField = $formType.GetField('_serviceError', [Reflection.BindingFlags]'Instance, NonPublic')
$refreshingField = $formType.GetField('_refreshing', [Reflection.BindingFlags]'Instance, NonPublic')
$updateToolTip = $formType.GetMethod('UpdateToolTip', [Reflection.BindingFlags]'Instance, NonPublic')
$toolTipField = $formType.GetField('_toolTip', [Reflection.BindingFlags]'Instance, NonPublic')
$trayIconMethod = $formType.GetMethod('CreatePercentageTrayIcon', [Reflection.BindingFlags]'Static, NonPublic')
$form = [Activator]::CreateInstance($formType, $true)
$claudeProvider = [Enum]::Parse($assembly.GetType('CodexUsageBar.UsageProvider'), 'Claude')
$claudeForm = [Activator]::CreateInstance($formType, @($claudeProvider))
$trayField = $formType.GetField('_trayIcon', [Reflection.BindingFlags]'Instance, NonPublic')
$trayField.GetValue($form).Visible = $false
$trayField.GetValue($claudeForm).Visible = $false
# Force motion only in these offscreen demonstration frames.
$formType.GetField('_attentionMotionEnabled', [Reflection.BindingFlags]'Instance, NonPublic').SetValue($form, $true)

function New-RoundedPath([Drawing.RectangleF]$rectangle, [single]$radius) {
    $diameter = $radius * 2
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rectangle.Left, $rectangle.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($rectangle.Right - $diameter, $rectangle.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($rectangle.Right - $diameter, $rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($rectangle.Left, $rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Fill-RoundedRect([Drawing.Graphics]$graphics, [Drawing.RectangleF]$rectangle, [single]$radius, [Drawing.Color]$color) {
    $path = New-RoundedPath $rectangle $radius
    $brush = New-Object Drawing.SolidBrush($color)
    try { $graphics.FillPath($brush, $path) }
    finally { $brush.Dispose(); $path.Dispose() }
}

function Draw-RoundedBorder([Drawing.Graphics]$graphics, [Drawing.RectangleF]$rectangle, [single]$radius, [Drawing.Color]$color, [single]$width) {
    $path = New-RoundedPath $rectangle $radius
    $pen = New-Object Drawing.Pen($color, $width)
    try { $graphics.DrawPath($pen, $path) }
    finally { $pen.Dispose(); $path.Dispose() }
}

function New-DemoSnapshot([double]$usedPercent, [double[]]$creditHours) {
    $now = [DateTime]::UtcNow
    $window = [Activator]::CreateInstance($windowType, $true)
    $windowType.GetProperty('UsedPercent').SetValue($window, $usedPercent, $null)
    $windowType.GetProperty('DurationMinutes').SetValue($window, 10080, $null)
    $windowType.GetProperty('ResetAtUtc').SetValue($window, $now.AddDays(5).AddHours(4), $null)

    $credits = [Activator]::CreateInstance($creditListType)
    foreach ($hours in $creditHours) {
        $credit = [Activator]::CreateInstance($creditType, $true)
        $creditType.GetProperty('Title').SetValue($credit, 'Full reset', $null)
        $creditType.GetProperty('ExpiresAtUtc').SetValue($credit, $now.AddHours($hours), $null)
        $credits.Add($credit)
    }

    $snapshot = [Activator]::CreateInstance($snapshotType, $true)
    $snapshotType.GetProperty('WeeklyWindow').SetValue($snapshot, $window, $null)
    $snapshotType.GetProperty('FetchedAtUtc').SetValue($snapshot, $now, $null)
    $snapshotType.GetProperty('PlanType').SetValue($snapshot, 'pro', $null)
    $snapshotType.GetProperty('OrdinaryUsageAllowed').SetValue($snapshot, $true, $null)
    $snapshotType.GetProperty('ResetCredits').SetValue($snapshot, $creditHours.Count, $null)
    $snapshotType.GetProperty('ResetCreditDetails').SetValue($snapshot, $credits, $null)
    return $snapshot
}

function Render-Widget($snapshot, [bool]$darkMode, [single]$phase, [bool]$claude = $false, [int]$scale = 4) {
    $targetForm = if ($claude) { $claudeForm } else { $form }
    $snapshotField.SetValue($targetForm, $snapshot)
    $darkModeField.SetValue($targetForm, $darkMode)
    $targetForm.BackColor = $formType.GetProperty('BackgroundColor', [Reflection.BindingFlags]'Instance, NonPublic').GetValue($targetForm, $null)
    $phaseField.SetValue($targetForm, $phase)
    $serviceErrorField.SetValue($targetForm, $null)
    $refreshingField.SetValue($targetForm, $false)

    return [CodexUsageBar.DocumentationRenderer]::Render($targetForm, $scale)
}

function Draw-ScaledWidget([Drawing.Graphics]$graphics, [Drawing.Bitmap]$widget, [Drawing.RectangleF]$destination) {
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $shadow = [Drawing.RectangleF]::new($destination.X + 3, $destination.Y + 7, $destination.Width, $destination.Height)
    Fill-RoundedRect $graphics $shadow 18 ([Drawing.Color]::FromArgb(34, 0, 0, 0))
    $graphics.DrawImage($widget, $destination)
}

function New-Canvas([int]$width, [int]$height, [Drawing.Color]$background) {
    # Two physical pixels per layout pixel. Text and vectors render at 2x;
    # widget bitmaps are already 4x and are only ever downsampled.
    $bitmap = New-Object Drawing.Bitmap ($width * 2), ($height * 2), ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear($background)
    $graphics.ScaleTransform(2, 2)
    return @($bitmap, $graphics)
}

function Save-Png([Drawing.Bitmap]$bitmap, [string]$name) {
    $path = Join-Path $outputRoot $name
    $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    return $path
}

$fontTitle = New-Object Drawing.Font('Segoe UI', 38, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
$fontHeading = New-Object Drawing.Font('Segoe UI', 22, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
$fontBody = New-Object Drawing.Font('Segoe UI', 17, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$fontBodyBold = New-Object Drawing.Font('Segoe UI', 17, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
$fontSmall = New-Object Drawing.Font('Segoe UI', 13, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$fontSmallBold = New-Object Drawing.Font('Segoe UI', 12, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
$fontGifTitle = New-Object Drawing.Font('Segoe UI', 27, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
$fontGifBody = New-Object Drawing.Font('Segoe UI', 14, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)

$ink = [Drawing.Color]::FromArgb(25, 28, 34)
$muted = [Drawing.Color]::FromArgb(92, 98, 108)
$blue = [Drawing.Color]::FromArgb(52, 101, 230)
$orange = [Drawing.Color]::FromArgb(210, 99, 23)
$lightCanvas = [Drawing.Color]::FromArgb(242, 243, 245)
$darkCanvas = [Drawing.Color]::FromArgb(16, 19, 25)
$darkInk = [Drawing.Color]::FromArgb(244, 245, 247)
$darkMuted = [Drawing.Color]::FromArgb(166, 172, 184)

$normalSnapshot = New-DemoSnapshot 24 @([double]168)
$urgentSnapshot = New-DemoSnapshot 31 @([double]8, [double]320, [double]344)
$criticalSnapshot = New-DemoSnapshot 31 @([double]0.75, [double]320, [double]344)
$claudeSnapshot = New-DemoSnapshot 14 @()
$sessionWindow = [Activator]::CreateInstance($windowType, $true)
$windowType.GetProperty('UsedPercent').SetValue($sessionWindow, [double]34, $null)
$windowType.GetProperty('DurationMinutes').SetValue($sessionWindow, 300, $null)
$windowType.GetProperty('ResetAtUtc').SetValue($sessionWindow, [DateTime]::UtcNow.AddHours(3), $null)
$snapshotType.GetProperty('SessionWindow').SetValue($claudeSnapshot, $sessionWindow, $null)
$snapshotType.GetProperty('PlanType').SetValue($claudeSnapshot, $null, $null)
$lightWidget = $null
$darkWidget = $null
$alertWidget = $null
$claudeLight = $null
$claudeDark = $null
$created = New-Object Collections.Generic.List[string]

try {
    $lightWidget = Render-Widget $normalSnapshot $false 0
    $darkWidget = Render-Widget $normalSnapshot $true 0
    $alertWidget = Render-Widget $urgentSnapshot $true 0.21
    $claudeLight = Render-Widget $claudeSnapshot $false 0 $true
    $claudeDark = Render-Widget $claudeSnapshot $true 0 $true

    $created.Add((Save-Png $lightWidget 'widget-light.png'))
    $created.Add((Save-Png $darkWidget 'widget-dark.png'))
    $created.Add((Save-Png $alertWidget 'widget-expiry-alert.png'))
    $created.Add((Save-Png $claudeLight 'widget-claude-light.png'))
    $created.Add((Save-Png $claudeDark 'widget-claude-dark.png'))
    $native = Render-Widget $normalSnapshot $false 0 $false 1
    try { $created.Add((Save-Png $native 'widget-light-native.png')) } finally { $native.Dispose() }

    # 01 - Overview / social preview
    $pair = New-Canvas 1280 640 $lightCanvas
    $overview = $pair[0]
    $g = $pair[1]
    try {
        $g.DrawString('Agent Usage Bar', $fontTitle, (New-Object Drawing.SolidBrush($ink)), 70, 48)
        $g.DrawString('Codex and Claude Code. Two small bars, one less thing to keep checking.', $fontBody, (New-Object Drawing.SolidBrush($muted)), 72, 102)
        Fill-RoundedRect $g ([Drawing.RectangleF]::new(72, 160, 318, 34)) 17 ([Drawing.Color]::FromArgb(226, 232, 248))
        $g.DrawString('WINDOWS  -  OPEN SOURCE  -  DEMO DATA', $fontSmallBold, (New-Object Drawing.SolidBrush($blue)), 88, 169)

        Fill-RoundedRect $g ([Drawing.RectangleF]::new(60, 220, 560, 348)) 22 ([Drawing.Color]::White)
        Draw-RoundedBorder $g ([Drawing.RectangleF]::new(60.5, 220.5, 559, 347)) 22 ([Drawing.Color]::FromArgb(218, 220, 224)) 1
        $g.DrawString('Light mode', $fontHeading, (New-Object Drawing.SolidBrush($ink)), 90, 245)
        Draw-ScaledWidget $g $lightWidget ([Drawing.RectangleF]::new(119, 301, 441.6, 102.4))
        Draw-ScaledWidget $g $claudeLight ([Drawing.RectangleF]::new(119, 431, 441.6, 102.4))

        Fill-RoundedRect $g ([Drawing.RectangleF]::new(660, 220, 560, 348)) 22 ([Drawing.Color]::FromArgb(30, 32, 36))
        $g.DrawString('Dark mode', $fontHeading, (New-Object Drawing.SolidBrush($darkInk)), 690, 245)
        Draw-ScaledWidget $g $darkWidget ([Drawing.RectangleF]::new(719, 301, 441.6, 102.4))
        Draw-ScaledWidget $g $claudeDark ([Drawing.RectangleF]::new(719, 431, 441.6, 102.4))

        $g.DrawString('Move them separately. Hide either one. Keep the percentages in your system tray.', $fontSmall, (New-Object Drawing.SolidBrush($muted)), 70, 597)
        $created.Add((Save-Png $overview '01-overview.png'))
    }
    finally { $g.Dispose(); $overview.Dispose() }

    # 02 - Expiry alert and hover details
    $darkModeField.SetValue($form, $true)
    $snapshotField.SetValue($form, $urgentSnapshot)
    $updateToolTip.Invoke($form, @()) | Out-Null
    $toolTip = $toolTipField.GetValue($form)
    $toolTipLines = $toolTip.GetToolTip($form) -split [Environment]::NewLine

    $pair = New-Canvas 1280 720 $darkCanvas
    $expiry = $pair[0]
    $g = $pair[1]
    try {
        $g.DrawString('Know before a banked reset expires', $fontTitle, (New-Object Drawing.SolidBrush($darkInk)), 64, 52)
        $g.DrawString('A local-time countdown appears at a glance; exact dates stay one hover away.', $fontBody, (New-Object Drawing.SolidBrush($darkMuted)), 66, 108)

        Fill-RoundedRect $g ([Drawing.RectangleF]::new(64, 180, 548, 420)) 24 ([Drawing.Color]::FromArgb(24, 28, 36))
        $g.DrawString('FINAL 24 HOURS', $fontSmallBold, (New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(244, 157, 72))), 94, 218)
        Draw-ScaledWidget $g $alertWidget ([Drawing.RectangleF]::new(82, 285, 510.6, 118.4))
        $g.DrawString('A small ember travels around the edge.', $fontBodyBold, (New-Object Drawing.SolidBrush($darkInk)), 94, 455)
        $g.DrawString('It stops when hidden and respects Windows motion settings.', $fontSmall, (New-Object Drawing.SolidBrush($darkMuted)), 94, 491)
        $g.DrawString('Final hour: the border turns red and the ember moves faster.', $fontSmall, (New-Object Drawing.SolidBrush($darkMuted)), 94, 520)

        Fill-RoundedRect $g ([Drawing.RectangleF]::new(650, 150, 566, 500)) 22 ([Drawing.Color]::FromArgb(28, 33, 43))
        Draw-RoundedBorder $g ([Drawing.RectangleF]::new(650.5, 150.5, 565, 499)) 22 ([Drawing.Color]::FromArgb(55, 63, 78)) 1
        $g.DrawString('HOVER DETAILS', $fontSmallBold, (New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(124, 156, 255))), 682, 180)
        Fill-RoundedRect $g ([Drawing.RectangleF]::new(1058, 176, 120, 30)) 15 ([Drawing.Color]::FromArgb(50, 56, 68))
        $g.DrawString('DEMO DATA', $fontSmallBold, (New-Object Drawing.SolidBrush($darkMuted)), 1073, 183)

        $y = 226
        foreach ($line in $toolTipLines) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $font = $fontSmall
            $color = $darkMuted
            if ($line.StartsWith('Banked resets:')) { $font = $fontBodyBold; $color = $darkInk }
            elseif ($line.StartsWith('Reset ')) { $font = $fontSmallBold; $color = $darkInk }
            elseif ($line.StartsWith('  ')) { $font = $fontSmallBold; $color = [Drawing.Color]::FromArgb(244, 157, 72) }
            elseif ($line.StartsWith('WEEKLY:')) { $font = $fontSmallBold; $color = $darkInk }
            $brush = New-Object Drawing.SolidBrush($color)
            try { $g.DrawString($line.TrimEnd(), $font, $brush, 682, $y) }
            finally { $brush.Dispose() }
            $lineSpacing = if ($line.StartsWith('Reset ')) { 29 } else { 25 }
            $y += $lineSpacing
        }
        $created.Add((Save-Png $expiry '02-reset-expiry.png'))
    }
    finally { $g.Dispose(); $expiry.Dispose() }

    # 03 - Windows taskbar context. Deliberately labelled as a mock, not a capture.
    $pair = New-Canvas 1280 640 $lightCanvas
    $tray = $pair[0]
    $g = $pair[1]
    $trayBitmaps = New-Object Collections.Generic.List[Drawing.Bitmap]
    try {
        $g.DrawString('The percentage is in your taskbar, too.', $fontTitle, (New-Object Drawing.SolidBrush($ink)), 70, 48)
        $g.DrawString('Keep an eye on both providers, even with the floating bars hidden.', $fontBody, (New-Object Drawing.SolidBrush($muted)), 72, 105)
        $g.DrawString('TASKBAR MOCK / SAMPLE DATA / REAL APP ICON RENDERER', $fontSmallBold, (New-Object Drawing.SolidBrush($blue)), 74, 153)
        Fill-RoundedRect $g ([Drawing.RectangleF]::new(70, 202, 1140, 334)) 18 ([Drawing.Color]::FromArgb(230, 234, 237))

        $codexTray = [CodexUsageBar.DocumentationRenderer]::RenderTray(76, 0, $false, 8)
        $claudeTray = [CodexUsageBar.DocumentationRenderer]::RenderTray(66, 0, $true, 8)
        $trayBitmaps.Add($codexTray)
        $trayBitmaps.Add($claudeTray)
        Fill-RoundedRect $g ([Drawing.RectangleF]::new(128, 244, 960, 190)) 14 ([Drawing.Color]::White)
        $g.DrawString('Icon close-up', $fontSmall, (New-Object Drawing.SolidBrush($muted)), 162, 267)
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.DrawImage($codexTray, [Drawing.RectangleF]::new(166, 308, 72, 72))
        $g.DrawString('Codex', $fontHeading, (New-Object Drawing.SolidBrush($ink)), 260, 313)
        $g.DrawString('76% weekly remaining', $fontBody, (New-Object Drawing.SolidBrush($muted)), 260, 349)
        $g.DrawImage($claudeTray, [Drawing.RectangleF]::new(628, 308, 72, 72))
        $g.DrawString('Claude Code', $fontHeading, (New-Object Drawing.SolidBrush($ink)), 723, 313)
        $g.DrawString('66% five-hour remaining', $fontBody, (New-Object Drawing.SolidBrush($muted)), 723, 349)

        # Familiar Windows 11 notification-area layout, without a user's apps,
        # desktop, account details, or real clock appearing in public media.
        Fill-RoundedRect $g ([Drawing.RectangleF]::new(70, 478, 1140, 58)) 12 ([Drawing.Color]::FromArgb(249, 249, 249))
        $g.DrawString('Windows system tray', $fontSmall, (New-Object Drawing.SolidBrush($muted)), 98, 500)
        $g.DrawString('^', $fontBody, (New-Object Drawing.SolidBrush($muted)), 871, 499)
        $g.DrawImage($codexTray, [Drawing.RectangleF]::new(906, 496, 20, 20))
        $g.DrawImage($claudeTray, [Drawing.RectangleF]::new(940, 496, 20, 20))
        $g.DrawString('ENG', $fontSmallBold, (New-Object Drawing.SolidBrush($ink)), 982, 500)
        $taskbarPen = New-Object Drawing.Pen($ink, 1.5)
        try {
            $g.DrawArc($taskbarPen, 1034, 496, 20, 17, 219, 103)
            $g.DrawArc($taskbarPen, 1038, 501, 12, 10, 219, 103)
            $g.FillEllipse((New-Object Drawing.SolidBrush($ink)), 1043, 508, 3, 3)
            $g.DrawRectangle($taskbarPen, 1068, 500, 5, 9)
            $g.DrawLine($taskbarPen, 1073, 500, 1079, 496)
            $g.DrawLine($taskbarPen, 1079, 496, 1079, 513)
            $g.DrawLine($taskbarPen, 1079, 513, 1073, 509)
            $g.DrawArc($taskbarPen, 1078, 496, 11, 17, 298, 124)
            $g.DrawLine($taskbarPen, 920, 443, 933, 479)
            $g.DrawLine($taskbarPen, 933, 479, 936, 470)
            $g.DrawLine($taskbarPen, 933, 479, 925, 475)
        } finally { $taskbarPen.Dispose() }
        $g.DrawString('12:00 pm', $fontSmall, (New-Object Drawing.SolidBrush($ink)), 1118, 491)
        $g.DrawString('21/09/2026', $fontSmall, (New-Object Drawing.SolidBrush($ink)), 1109, 508)
        $g.DrawString('Double-click an icon to bring its bar back. Right-click for options.', $fontBody, (New-Object Drawing.SolidBrush($muted)), 74, 575)
        $created.Add((Save-Png $tray '03-tray-status.png'))
    }
    finally {
        foreach ($bitmap in $trayBitmaps) { $bitmap.Dispose() }
        $g.Dispose()
        $tray.Dispose()
    }

    # 04 - Two real animation states, at the app's actual 10 fps phase increments.
    # Frames live in ignored dist; no account state or screen capture is read.
    if (-not $SkipAnimation) {
        $framesRoot = Join-Path $renderRoot 'expiry-frames'
        New-Item -ItemType Directory -Force -Path $framesRoot | Out-Null
        for ($frameIndex = 0; $frameIndex -lt 80; $frameIndex++) {
            $critical = $frameIndex -ge 40
            $phase = if ($critical) { [single]((($frameIndex - 40) * 0.04) % 1) } else { [single]($frameIndex * 0.025) }
            $demo = if ($critical) { $criticalSnapshot } else { $urgentSnapshot }
            $snapshotType.GetProperty('FetchedAtUtc').SetValue($demo, [DateTime]::UtcNow, $null)
            $widgetFrame = Render-Widget $demo $true $phase
            $pair = New-Canvas 840 320 $darkCanvas
            $frame = $pair[0]
            $g = $pair[1]
            try {
                $g.DrawString('A heads-up before your reset expires.', $fontGifTitle, (New-Object Drawing.SolidBrush($darkInk)), 42, 24)
                $caption = if ($critical) { 'FINAL HOUR: red border, faster ember. Same small widget.' } else { 'FINAL 24 HOURS: an amber ember moves around the bar.' }
                $g.DrawString($caption, $fontGifBody, (New-Object Drawing.SolidBrush($darkMuted)), 44, 67)
                $g.DrawString('DEMO DATA', $fontSmallBold, (New-Object Drawing.SolidBrush($darkMuted)), 708, 33)
                Draw-ScaledWidget $g $widgetFrame ([Drawing.RectangleF]::new(144, 119, 552, 128))
                $g.DrawString('10 fps. Pauses while hidden. Respects Windows motion settings.', $fontGifBody, (New-Object Drawing.SolidBrush($darkMuted)), 44, 280)
                $frame.Save((Join-Path $framesRoot ('frame-{0:D3}.png' -f $frameIndex)), [Drawing.Imaging.ImageFormat]::Png)
                if ($frameIndex -eq 12) { $created.Add((Save-Png $frame '04-reset-expiry-poster.png')) }
            }
            finally {
                $widgetFrame.Dispose()
                $g.Dispose()
                $frame.Dispose()
            }
        }
        $gifPath = Join-Path $outputRoot '04-reset-expiry-animation.gif'
        & $PythonPath (Join-Path $PSScriptRoot 'encode-docs-gif.py') $framesRoot $gifPath
        if ($LASTEXITCODE -ne 0) { throw 'GIF encoding failed. Use Python with Pillow, or -SkipAnimation for stills only.' }
        $created.Add($gifPath)
    }

    $created | ForEach-Object { Get-Item -LiteralPath $_ } | Select-Object Name, Length, FullName
}
finally {
    if ($lightWidget) { $lightWidget.Dispose() }
    if ($darkWidget) { $darkWidget.Dispose() }
    if ($alertWidget) { $alertWidget.Dispose() }
    if ($claudeLight) { $claudeLight.Dispose() }
    if ($claudeDark) { $claudeDark.Dispose() }
    $fontTitle.Dispose()
    $fontHeading.Dispose()
    $fontBody.Dispose()
    $fontBodyBold.Dispose()
    $fontSmall.Dispose()
    $fontSmallBold.Dispose()
    $fontGifTitle.Dispose()
    $fontGifBody.Dispose()
    $form.Dispose()
    $claudeForm.Dispose()
}
