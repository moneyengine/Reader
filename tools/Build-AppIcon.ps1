Add-Type -AssemblyName System.Drawing
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $stream = [System.IO.MemoryStream]::new()
    $blue = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(30, 103, 239))
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.ScaleTransform($size / 32.0, $size / 32.0)
        $graphics.FillEllipse($blue, 1, 1, 30, 30)
        $left = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(6,9), [System.Drawing.PointF]::new(11,8),
            [System.Drawing.PointF]::new(15,10), [System.Drawing.PointF]::new(15,24),
            [System.Drawing.PointF]::new(11,22), [System.Drawing.PointF]::new(6,23))
        $right = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(17,10), [System.Drawing.PointF]::new(21,8),
            [System.Drawing.PointF]::new(26,9), [System.Drawing.PointF]::new(26,23),
            [System.Drawing.PointF]::new(21,22), [System.Drawing.PointF]::new(17,24))
        $graphics.FillPolygon([System.Drawing.Brushes]::White, $left)
        $graphics.FillPolygon([System.Drawing.Brushes]::White, $right)
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        ,$stream.ToArray()
    } finally { $blue.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose() }
}
$folder = Join-Path $PSScriptRoot '../src/Assets'
[System.IO.Directory]::CreateDirectory($folder) | Out-Null
$file = [System.IO.File]::Create((Join-Path $folder 'LightReader.ico'))
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
