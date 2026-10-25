$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetFolder = Join-Path $PSScriptRoot 'Assets'
New-Item -ItemType Directory -Force $assetFolder | Out-Null
$images = @()
foreach ($size in @(16,24,32,48,64,256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size,$size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($size/64.0,$size/64.0)
    $bg = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255,34,112,165))
    $white = [System.Drawing.Pen]::new([System.Drawing.Color]::White,4)
    $white.StartCap = $white.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.FillEllipse($bg,1,1,62,62)
    $g.DrawRectangle($white,19,18,27,34)
    $g.FillRectangle($bg,25,14,15,10)
    $g.DrawRectangle($white,25,14,15,10)
    $g.DrawLine($white,26,33,39,33)
    $g.DrawLine($white,26,41,36,41)
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,$stream.ToArray()
    $stream.Dispose(); $white.Dispose(); $bg.Dispose(); $g.Dispose(); $bitmap.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $assetFolder 'ClipShot.ico'))
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16*$images.Count
for ($i=0; $i -lt $images.Count; $i++) {
    $size = @(16,24,32,48,64,256)[$i]
    $edge = if ($size -eq 256) {0} else {$size}
    $writer.Write([byte]$edge); $writer.Write([byte]$edge); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($bytes in $images) {$writer.Write([byte[]]$bytes)}
$writer.Dispose(); $file.Dispose()
