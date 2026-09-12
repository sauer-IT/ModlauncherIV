<#
.SYNOPSIS
  Produces the application icon as an .ico.

.DESCRIPTION
  Drawn rather than painted: the icon comes out of geometry, not out of an image
  file somebody once scaled in a graphics program. The difference shows at 16
  pixels - there a downscaled drawing does not survive, a freshly drawn one does.

  The mark is an "IV" in the colours the trainer menu in game and the launcher
  use as well: dark ground, orange stroke. The two visibly belong together.

  Letters are drawn as strokes, not set as text. A typeface would be mush at 16
  pixels, and it would also be a dependency - which fonts are installed is not
  something you know about someone else's machine.

  Runs under Windows PowerShell 5.1, because System.Drawing ships with it.
  Under PowerShell 7 it is missing.

.EXAMPLE
  .\scripts\make-icon.ps1
#>
[CmdletBinding()]
param(
    [string] $Out = (Join-Path (Split-Path -Parent $PSScriptRoot) "assets\ModlauncherIV.ico")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

# The same colours as in Theme.xaml and in the trainer menu.
$back   = [System.Drawing.ColorTranslator]::FromHtml("#16181C")
$accent = [System.Drawing.ColorTranslator]::FromHtml("#E0A04A")
$edge   = [System.Drawing.ColorTranslator]::FromHtml("#9E5710")

function New-RoundedPath([float] $x, [float] $y, [float] $w, [float] $h, [float] $r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Bitmap([int] $size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)

    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float] $size

    # The tile. The border grows with the size, otherwise it disappears entirely
    # at 16 pixels or eats half the icon at 256.
    $inset = $s * 0.04
    $tile = New-RoundedPath $inset $inset ($s - 2 * $inset) ($s - 2 * $inset) ($s * 0.20)

    $fill = New-Object System.Drawing.SolidBrush($back)
    $g.FillPath($fill, $tile)

    $border = New-Object System.Drawing.Pen($edge, [float] ([Math]::Max(1.0, $s * 0.045)))
    $g.DrawPath($border, $tile)

    # "IV" as strokes. Round caps and joins, so that small sizes do not produce
    # frayed tips.
    $pen = New-Object System.Drawing.Pen($accent, [float] ($s * 0.115))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    # The I.
    $g.DrawLine($pen, $s * 0.31, $s * 0.31, $s * 0.31, $s * 0.69)

    # The V.
    $v = @(
        (New-Object System.Drawing.PointF(($s * 0.48), ($s * 0.31))),
        (New-Object System.Drawing.PointF(($s * 0.61), ($s * 0.69))),
        (New-Object System.Drawing.PointF(($s * 0.74), ($s * 0.31)))
    )
    $g.DrawLines($pen, [System.Drawing.PointF[]] $v)

    $pen.Dispose(); $border.Dispose(); $fill.Dispose(); $tile.Dispose(); $g.Dispose()
    return $bmp
}

# Turns a bitmap into the payload an ICO entry expects: a BITMAPINFOHEADER,
# below it the pixels bottom-up, below that the AND mask.
#
# That is more work than dropping a PNG in - but not everything reads PNG inside
# an ICO. The Icon class of the .NET Framework fails on it, and with it
# everything that uses the class. So for sizes up to 128, the old, universally
# readable way.
function ConvertTo-IcoDib([System.Drawing.Bitmap] $bmp) {
    $w = $bmp.Width
    $h = $bmp.Height

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    # BITMAPINFOHEADER. The height counts double: image and mask stacked.
    $writer.Write([uint32] 40)
    $writer.Write([int32] $w)
    $writer.Write([int32] ($h * 2))
    $writer.Write([uint16] 1)
    $writer.Write([uint16] 32)
    $writer.Write([uint32] 0)              # no compression
    $writer.Write([uint32] ($w * $h * 4))
    $writer.Write([int32] 0); $writer.Write([int32] 0)
    $writer.Write([uint32] 0); $writer.Write([uint32] 0)

    # The pixels, last row first.
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $writer.Write([byte] $c.B)
            $writer.Write([byte] $c.G)
            $writer.Write([byte] $c.R)
            $writer.Write([byte] $c.A)
        }
    }

    # The AND mask. At 32 bits the alpha channel decides, but the entry still has
    # to be there - and padded to four bytes per row at that.
    $rowBytes = [math]::Ceiling($w / 8.0)
    $padded = [int]([math]::Ceiling($rowBytes / 4.0) * 4)

    for ($y = 0; $y -lt $h; $y++) {
        for ($b = 0; $b -lt $padded; $b++) { $writer.Write([byte] 0) }
    }

    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose(); $stream.Dispose()

    # The comma is necessary, not decoration: without it PowerShell unrolls the
    # array on return into single bytes, and the caller ends up holding an
    # object[] instead of a byte[]. The difference only shows once nobody can
    # read the finished .ico any more.
    return ,$bytes
}

# The sizes Windows actually asks for: Explorer small and large, the taskbar,
# Alt-Tab and the tile view.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

$images = @()
foreach ($size in $sizes) {
    $bmp = New-Bitmap $size

    if ($size -ge 256) {
        # At 256 PNG is the usual choice and saves around 250 KB against BMP.
        $stream = New-Object System.IO.MemoryStream
        $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $stream.ToArray()
        $stream.Dispose()
    }
    else {
        $bytes = ConvertTo-IcoDib $bmp
    }

    $images += ,@{ Size = $size; Bytes = $bytes }
    $bmp.Dispose()
}

# ------------------------------------------------------------- Writing the ICO
#
# The container format is plain: a header, one directory entry per image, then
# the image data in one piece.

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
$file = [System.IO.File]::Create($Out)
$writer = New-Object System.IO.BinaryWriter($file)

$writer.Write([uint16] 0)                  # reserved
$writer.Write([uint16] 1)                  # type 1 = icon
$writer.Write([uint16] $images.Count)

# The data starts behind the header and all directory entries.
$offset = 6 + 16 * $images.Count

foreach ($image in $images) {
    # 256 is written as 0 - one byte does not hold the number.
    $dim = if ($image.Size -ge 256) { 0 } else { $image.Size }

    $writer.Write([byte] $dim)             # width
    $writer.Write([byte] $dim)             # height
    $writer.Write([byte] 0)                # colours in the palette (0 = none)
    $writer.Write([byte] 0)                # reserved
    $writer.Write([uint16] 1)              # planes
    $writer.Write([uint16] 32)             # bits per pixel
    $writer.Write([uint32] $image.Bytes.Length)
    $writer.Write([uint32] $offset)

    $offset += $image.Bytes.Length
}

foreach ($image in $images) { $writer.Write($image.Bytes) }

$writer.Flush(); $writer.Dispose(); $file.Dispose()

$kb = [math]::Round((Get-Item $Out).Length / 1KB, 1)
Write-Host "Icon written: $Out ($kb KB, $($images.Count) sizes)" -ForegroundColor Green
