$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$source = [Drawing.Image]::FromFile((Join-Path $root 'src/DailyRecord.App/Assets/e2-day-logo.png'))
$frames = @()
try {
    foreach ($size in @(16,32,48,256)) {
        $bitmap = [Drawing.Bitmap]::new($size,$size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(33,111,166))
        $font = [Drawing.Font]::new('Microsoft YaHei UI',[single]($size * 0.29),[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
        try {
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.DrawImage($source,0,0,$size,$size)
            $graphics.FillEllipse($brush,[single]($size * 0.50),[single]($size * 0.50),[single]($size * 0.50),[single]($size * 0.50))
            $format = [Drawing.StringFormat]::new(); $format.Alignment = 'Center'; $format.LineAlignment = 'Center'
            try { $graphics.DrawString('教',$font,[Drawing.Brushes]::White,[Drawing.RectangleF]::new($size * 0.50,$size * 0.50,$size * 0.50,$size * 0.50),$format) } finally { $format.Dispose() }
            $memory = [IO.MemoryStream]::new()
            try { $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png); $frames += ,($memory.ToArray()) } finally { $memory.Dispose() }
            if ($size -eq 256) { $bitmap.Save((Join-Path $root 'src/DailyRecord.Tutor/Assets/e2-day-logo.png'),[Drawing.Imaging.ImageFormat]::Png) }
        } finally { $graphics.Dispose(); $font.Dispose(); $brush.Dispose(); $bitmap.Dispose() }
    }
} finally { $source.Dispose() }
$stream = [IO.File]::Create((Join-Path $root 'src/DailyRecord.Tutor/Assets/e2-day-tutor.ico'))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]4)
    $offset = 6 + 16 * 4
    for ($index = 0; $index -lt 4; $index++) {
        $size = @(16,32,48,0)[$index]
        $writer.Write([byte]$size); $writer.Write([byte]$size); $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset); $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $stream.Dispose() }
