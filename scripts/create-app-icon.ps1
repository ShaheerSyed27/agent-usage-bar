param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sizes = @(16,24,32,48,64,128,256)
$images = foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $fill = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(52,101,230))
    $white = [Drawing.SolidBrush]::new([Drawing.Color]::White)
    $ring = [Drawing.Pen]::new([Drawing.Color]::White,[single]2.7)
    $stream = [IO.MemoryStream]::new()
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.ScaleTransform([single]($size/32),[single]($size/32))
        $graphics.FillEllipse($fill,1,1,30,30)
        $graphics.DrawArc($ring,7,7,18,18,-52,278)
        $graphics.FillEllipse($white,[single]21,[single]8,[single]3.5,[single]3.5)
        $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
        ,$stream.ToArray()
    } finally {
        $stream.Dispose(); $ring.Dispose(); $white.Dispose(); $fill.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    }
}
$file = [IO.File]::Create($OutputPath)
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
    $offset = 6 + 16*$sizes.Count
    for ($index=0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) {0} else {$sizes[$index]}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([UInt16]1); $writer.Write([UInt16]32)
        $writer.Write([UInt32]$images[$index].Length); $writer.Write([UInt32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
