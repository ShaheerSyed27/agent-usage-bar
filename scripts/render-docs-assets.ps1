$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not ('AnimatedGifWriter' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing.dll -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Serialization;

public static class AnimatedGifWriter
{
    private static PropertyItem NewPropertyItem(int id, short type, byte[] value)
    {
        PropertyItem item = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
        item.Id = id;
        item.Type = type;
        item.Len = value.Length;
        item.Value = value;
        return item;
    }

    public static void Save(IList<Bitmap> frames, string path, int delayHundredths)
    {
        if (frames == null || frames.Count == 0) throw new ArgumentException("At least one frame is required.");

        ImageCodecInfo encoder = null;
        foreach (ImageCodecInfo candidate in ImageCodecInfo.GetImageEncoders())
        {
            if (candidate.FormatID == ImageFormat.Gif.Guid)
            {
                encoder = candidate;
                break;
            }
        }
        if (encoder == null) throw new InvalidOperationException("GIF encoder is unavailable.");

        byte[] delays = new byte[frames.Count * 4];
        for (int index = 0; index < frames.Count; index++)
        {
            byte[] delay = BitConverter.GetBytes(delayHundredths);
            Buffer.BlockCopy(delay, 0, delays, index * 4, 4);
        }

        Bitmap first = frames[0];
        first.SetPropertyItem(NewPropertyItem(0x5100, 4, delays));
        first.SetPropertyItem(NewPropertyItem(0x5101, 3, new byte[] { 0, 0 }));

        using (EncoderParameters parameters = new EncoderParameters(1))
        {
            parameters.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.MultiFrame);
            first.Save(path, encoder, parameters);

            parameters.Param[0].Dispose();
            for (int index = 1; index < frames.Count; index++)
            {
                parameters.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.FrameDimensionTime);
                first.SaveAdd(frames[index], parameters);
                parameters.Param[0].Dispose();
            }

            parameters.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.Flush);
            first.SaveAdd(parameters);
        }
    }
}
'@
}

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$assemblyPath = Join-Path $projectRoot 'dist\AgentUsageBar.exe'
$outputRoot = Join-Path $projectRoot 'docs\screenshots'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Build the application before rendering documentation assets: $assemblyPath"
}

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

function Render-Widget($snapshot, [bool]$darkMode, [single]$phase, [bool]$claude = $false) {
    $targetForm = if ($claude) { $claudeForm } else { $form }
    $snapshotField.SetValue($targetForm, $snapshot)
    $darkModeField.SetValue($targetForm, $darkMode)
    $targetForm.BackColor = $formType.GetProperty('BackgroundColor', [Reflection.BindingFlags]'Instance, NonPublic').GetValue($targetForm, $null)
    $phaseField.SetValue($targetForm, $phase)
    $serviceErrorField.SetValue($targetForm, $null)
    $refreshingField.SetValue($targetForm, $false)

    $raw = New-Object Drawing.Bitmap 276, 64, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $targetForm.DrawToBitmap($raw, [Drawing.Rectangle]::new(0, 0, 276, 64))
    $masked = New-Object Drawing.Bitmap 276, 64, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($masked)
    $clip = New-RoundedPath ([Drawing.RectangleF]::new(0, 0, 276, 64)) 10
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.SetClip($clip)
        $graphics.DrawImageUnscaled($raw, 0, 0)
    }
    finally {
        $clip.Dispose()
        $graphics.Dispose()
        $raw.Dispose()
    }
    return $masked
}

function Draw-ScaledWidget([Drawing.Graphics]$graphics, [Drawing.Bitmap]$widget, [Drawing.RectangleF]$destination) {
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $shadow = [Drawing.RectangleF]::new($destination.X + 3, $destination.Y + 7, $destination.Width, $destination.Height)
    Fill-RoundedRect $graphics $shadow 18 ([Drawing.Color]::FromArgb(34, 0, 0, 0))
    $graphics.DrawImage($widget, $destination)
}

function New-Canvas([int]$width, [int]$height, [Drawing.Color]$background) {
    $bitmap = New-Object Drawing.Bitmap $width, $height, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $graphics.Clear($background)
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
        $g.DrawString('It stops when the widget is hidden and respects Windows motion settings.', $fontSmall, (New-Object Drawing.SolidBrush($darkMuted)), 94, 491)
        $g.DrawString('The countdown remains readable without hovering.', $fontSmall, (New-Object Drawing.SolidBrush($darkMuted)), 94, 520)

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

    # 03 - Tray status states
    $pair = New-Canvas 1280 500 $lightCanvas
    $tray = $pair[0]
    $g = $pair[1]
    $trayBitmaps = New-Object Collections.Generic.List[Drawing.Bitmap]
    try {
        $g.DrawString('Readable at tray size', $fontTitle, (New-Object Drawing.SolidBrush($ink)), 70, 54)
        $g.DrawString('The number is primary; color adds status context.', $fontBody, (New-Object Drawing.SolidBrush($muted)), 72, 109)
        Fill-RoundedRect $g ([Drawing.RectangleF]::new(70, 176, 1140, 220)) 24 ([Drawing.Color]::White)
        Draw-RoundedBorder $g ([Drawing.RectangleF]::new(70.5, 176.5, 1139, 219)) 24 ([Drawing.Color]::FromArgb(216, 218, 222)) 1

        $tests = @(@(100, 0, 'Full'), @(72, 0, 'Healthy'), @(25, 1, 'Low'), @(9, 2, 'Critical'))
        for ($index = 0; $index -lt $tests.Count; $index++) {
            $percent = $tests[$index][0]
            $state = $tests[$index][1]
            $label = $tests[$index][2]
            $icon = $trayIconMethod.Invoke($null, @($percent, $state, $false))
            try { $iconBitmap = $icon.ToBitmap() }
            finally { $icon.Dispose() }
            $trayBitmaps.Add($iconBitmap)
            $x = 150 + ($index * 270)
            $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
            $g.DrawImage($iconBitmap, [Drawing.Rectangle]::new($x, 226, 80, 80), 0, 0, 20, 20, [Drawing.GraphicsUnit]::Pixel)
            $g.DrawImageUnscaled($iconBitmap, $x + 101, 256)
            $g.DrawString("$percent%", $fontHeading, (New-Object Drawing.SolidBrush($ink)), $x, 320)
            $g.DrawString($label, $fontSmall, (New-Object Drawing.SolidBrush($muted)), $x, 352)
        }
        $g.DrawString('Large preview', $fontSmall, (New-Object Drawing.SolidBrush($muted)), 148, 409)
        $g.DrawString('Actual 20 px', $fontSmall, (New-Object Drawing.SolidBrush($muted)), 247, 409)
        $created.Add((Save-Png $tray '03-tray-status.png'))
    }
    finally {
        foreach ($bitmap in $trayBitmaps) { $bitmap.Dispose() }
        $g.Dispose()
        $tray.Dispose()
    }

    # 04 - Animated expiry alert
    $frames = New-Object Collections.Generic.List[Drawing.Bitmap]
    try {
        for ($frameIndex = 0; $frameIndex -lt 32; $frameIndex++) {
            $phase = [single]($frameIndex / 32.0)
            $widgetFrame = Render-Widget $urgentSnapshot $true $phase
            $pair = New-Canvas 760 280 $darkCanvas
            $frame = $pair[0]
            $g = $pair[1]
            try {
                $g.DrawString('Reset expiry alert', $fontGifTitle, (New-Object Drawing.SolidBrush($darkInk)), 52, 30)
                $g.DrawString('A lightweight ember appears only inside the final 24 hours.', $fontGifBody, (New-Object Drawing.SolidBrush($darkMuted)), 54, 70)
                Draw-ScaledWidget $g $widgetFrame ([Drawing.RectangleF]::new(104, 118, 552, 128))
            }
            finally {
                $widgetFrame.Dispose()
                $g.Dispose()
            }
            $frames.Add($frame)
        }
        $gifPath = Join-Path $outputRoot '04-reset-expiry-animation.gif'
        [AnimatedGifWriter]::Save($frames, $gifPath, 10)
        $created.Add($gifPath)
    }
    finally {
        foreach ($frame in $frames) { $frame.Dispose() }
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
