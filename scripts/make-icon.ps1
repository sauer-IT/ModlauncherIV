<#
.SYNOPSIS
  Erzeugt das Programmsymbol als .ico.

.DESCRIPTION
  Gezeichnet statt gemalt: das Symbol entsteht aus Geometrie, nicht aus einer
  Bilddatei, die jemand irgendwann in einem Grafikprogramm skaliert hat. Der
  Unterschied faellt bei 16 Pixeln auf - dort ueberlebt eine herunterskalierte
  Zeichnung nicht, eine neu gezeichnete schon.

  Die Marke ist ein "IV" in den Farben, die auch das Trainer-Menue im Spiel und
  der Launcher benutzen: dunkler Grund, orangefarbener Strich. Beides gehoert
  sichtbar zusammen.

  Buchstaben werden als Striche gezogen, nicht als Text gesetzt. Eine Schrift
  waere bei 16 Pixeln Matsch, und sie waere ausserdem eine Abhaengigkeit -
  welche Schriften installiert sind, weiss man auf einem fremden Rechner nicht.

  Laeuft unter Windows PowerShell 5.1, weil dort System.Drawing zum Lieferumfang
  gehoert. Unter PowerShell 7 fehlt es.

.EXAMPLE
  .\scripts\make-icon.ps1
#>
[CmdletBinding()]
param(
    [string] $Out = (Join-Path (Split-Path -Parent $PSScriptRoot) "assets\ModlauncherIV.ico")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

# Dieselben Farben wie in Theme.xaml und im Trainer-Menue.
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

    # Kachel. Der Rand waechst mit der Groesse mit, sonst verschwindet er bei 16
    # Pixeln ganz oder frisst bei 256 das halbe Symbol.
    $inset = $s * 0.04
    $tile = New-RoundedPath $inset $inset ($s - 2 * $inset) ($s - 2 * $inset) ($s * 0.20)

    $fill = New-Object System.Drawing.SolidBrush($back)
    $g.FillPath($fill, $tile)

    $border = New-Object System.Drawing.Pen($edge, [float] ([Math]::Max(1.0, $s * 0.045)))
    $g.DrawPath($border, $tile)

    # "IV" als Striche. Runde Enden und Ecken, damit bei kleinen Groessen keine
    # ausgefransten Spitzen entstehen.
    $pen = New-Object System.Drawing.Pen($accent, [float] ($s * 0.115))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    # Das I.
    $g.DrawLine($pen, $s * 0.31, $s * 0.31, $s * 0.31, $s * 0.69)

    # Das V.
    $v = @(
        (New-Object System.Drawing.PointF(($s * 0.48), ($s * 0.31))),
        (New-Object System.Drawing.PointF(($s * 0.61), ($s * 0.69))),
        (New-Object System.Drawing.PointF(($s * 0.74), ($s * 0.31)))
    )
    $g.DrawLines($pen, [System.Drawing.PointF[]] $v)

    $pen.Dispose(); $border.Dispose(); $fill.Dispose(); $tile.Dispose(); $g.Dispose()
    return $bmp
}

# Wandelt ein Bitmap in die Nutzlast, die ein ICO-Eintrag erwartet: einen
# BITMAPINFOHEADER, darunter die Bildpunkte von unten nach oben, darunter die
# AND-Maske.
#
# Das ist mehr Arbeit als ein PNG hineinzulegen - aber PNG im ICO liest nicht
# jeder. Die Icon-Klasse von .NET Framework scheitert daran, und damit alles,
# was sie benutzt. Fuer Groessen bis 128 also der alte, ueberall lesbare Weg.
function ConvertTo-IcoDib([System.Drawing.Bitmap] $bmp) {
    $w = $bmp.Width
    $h = $bmp.Height

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    # BITMAPINFOHEADER. Die Hoehe zaehlt doppelt: Bild und Maske uebereinander.
    $writer.Write([uint32] 40)
    $writer.Write([int32] $w)
    $writer.Write([int32] ($h * 2))
    $writer.Write([uint16] 1)
    $writer.Write([uint16] 32)
    $writer.Write([uint32] 0)              # keine Kompression
    $writer.Write([uint32] ($w * $h * 4))
    $writer.Write([int32] 0); $writer.Write([int32] 0)
    $writer.Write([uint32] 0); $writer.Write([uint32] 0)

    # Bildpunkte, letzte Zeile zuerst.
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $writer.Write([byte] $c.B)
            $writer.Write([byte] $c.G)
            $writer.Write([byte] $c.R)
            $writer.Write([byte] $c.A)
        }
    }

    # Die AND-Maske. Bei 32 Bit entscheidet der Alphakanal, aber der Eintrag muss
    # trotzdem da sein - und zwar je Zeile auf vier Byte aufgefuellt.
    $rowBytes = [math]::Ceiling($w / 8.0)
    $padded = [int]([math]::Ceiling($rowBytes / 4.0) * 4)

    for ($y = 0; $y -lt $h; $y++) {
        for ($b = 0; $b -lt $padded; $b++) { $writer.Write([byte] 0) }
    }

    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose(); $stream.Dispose()

    # Das Komma ist noetig, nicht Zierde: ohne es entrollt PowerShell das Array
    # beim Zurueckgeben zu einzelnen Bytes, und der Aufrufer haelt am Ende ein
    # object[] statt eines byte[]. Der Unterschied faellt erst auf, wenn die
    # fertige .ico niemand mehr lesen kann.
    return ,$bytes
}

# Die Groessen, die Windows tatsaechlich abruft: Explorer klein und gross,
# Taskleiste, Alt-Tab und die Kachelansicht.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

$images = @()
foreach ($size in $sizes) {
    $bmp = New-Bitmap $size

    if ($size -ge 256) {
        # Bei 256 ist PNG das Uebliche und spart rund 250 KB gegenueber BMP.
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

# ---------------------------------------------------------------- ICO schreiben
#
# Das Containerformat ist schlicht: ein Kopf, je Bild ein Verzeichniseintrag,
# danach die Bilddaten am Stueck.

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
$file = [System.IO.File]::Create($Out)
$writer = New-Object System.IO.BinaryWriter($file)

$writer.Write([uint16] 0)                  # reserviert
$writer.Write([uint16] 1)                  # Typ 1 = Symbol
$writer.Write([uint16] $images.Count)

# Die Daten beginnen hinter Kopf und allen Verzeichniseintraegen.
$offset = 6 + 16 * $images.Count

foreach ($image in $images) {
    # 256 wird als 0 geschrieben - ein Byte fasst die Zahl nicht.
    $dim = if ($image.Size -ge 256) { 0 } else { $image.Size }

    $writer.Write([byte] $dim)             # Breite
    $writer.Write([byte] $dim)             # Hoehe
    $writer.Write([byte] 0)                # Farben in der Palette (0 = keine)
    $writer.Write([byte] 0)                # reserviert
    $writer.Write([uint16] 1)              # Ebenen
    $writer.Write([uint16] 32)             # Bits je Bildpunkt
    $writer.Write([uint32] $image.Bytes.Length)
    $writer.Write([uint32] $offset)

    $offset += $image.Bytes.Length
}

foreach ($image in $images) { $writer.Write($image.Bytes) }

$writer.Flush(); $writer.Dispose(); $file.Dispose()

$kb = [math]::Round((Get-Item $Out).Length / 1KB, 1)
Write-Host "Symbol geschrieben: $Out ($kb KB, $($images.Count) Groessen)" -ForegroundColor Green
