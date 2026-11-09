# Renders the StromForbrug icon in every size into StromForbrug.Web/wwwroot/icons:
# consumption bars (kWh at the bottom, kroner on top) under a yellow lightning bolt on a green tile.
#   powershell -File tools\make-icons.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "..\StromForbrug.Web\wwwroot\icons"
New-Item -ItemType Directory -Force $out | Out-Null

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $path
}

function Color([string]$hex, [int]$alpha = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    [System.Drawing.Color]::FromArgb($alpha, $c.R, $c.G, $c.B)
}

# The artwork is drawn on a 100 x 100 grid. $content shrinks it towards the centre (maskable
# icons must stay inside the safe zone); $rounded gives the background its own rounded corners
# (favicons and manifest icons); iOS and maskable icons are rounded by the OS.
function New-Icon([int]$size, [double]$content = 0.84, [bool]$rounded = $false) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $g.Clear([System.Drawing.Color]::Transparent)

    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (Color "#1f9a62"), (Color "#0b4a30"), 90
    if ($rounded) {
        $g.FillPath($bg, (New-RoundedRect 0 0 $size $size ($size * 0.22)))
    } else {
        $g.FillRectangle($bg, $rect)
    }

    $s = $size / 100.0 * $content
    $off = ($size - 100 * $s) / 2
    $g.TranslateTransform([float]$off, [float]$off)
    $g.ScaleTransform([float]$s, [float]$s)

    # Bars: [x, kWh height, kroner height]. The last two are muted.
    $bars = @(@(14, 22, 12), @(31, 36, 14), @(48, 16, 12), @(65, 28, 12), @(82, 20, 12))
    $baseline = 82
    $i = 0
    foreach ($b in $bars) {
        $tariff = if ($i -ge 3) { "#c9a24a" } else { "#f2c14e" }
        $spot = if ($i -ge 3) { "#7cc7a3" } else { "#a6e3c4" }
        $x = $b[0] - 6; $w = 12
        $spotTop = $baseline - $b[1]
        $top = $spotTop - $b[2]
        $g.FillPath((New-Object System.Drawing.SolidBrush (Color $tariff)), (New-RoundedRect $x $top $w ($b[2] + 4) 3))
        $g.FillRectangle((New-Object System.Drawing.SolidBrush (Color $spot)), [float]$x, [float]($spotTop + 1), [float]$w, [float]($b[1] - 1))
        $i++
    }
    $g.FillRectangle((New-Object System.Drawing.SolidBrush (Color "#d4f2e2")), 5, $baseline, 90, 2.5)

    # The bolt, outlined in the background blue so it stands clear of the bars.
    $bolt = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF 60, 6), (New-Object System.Drawing.PointF 34, 50),
        (New-Object System.Drawing.PointF 50, 50), (New-Object System.Drawing.PointF 42, 94),
        (New-Object System.Drawing.PointF 72, 42), (New-Object System.Drawing.PointF 56, 42),
        (New-Object System.Drawing.PointF 70, 6))
    $outline = New-Object System.Drawing.Pen (Color "#0b4a30"), 7
    $outline.LineJoin = "Round"
    $g.DrawPolygon($outline, $bolt)
    $g.FillPolygon((New-Object System.Drawing.SolidBrush (Color "#fab219")), $bolt)

    $g.Dispose()
    $bmp
}

function Save-Png($bmp, [string]$name) {
    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# iOS home screen (opaque, square: iOS rounds the corners itself).
Save-Png (New-Icon 180) "apple-touch-icon.png"
Save-Png (New-Icon 167) "apple-touch-icon-167.png"
Save-Png (New-Icon 152) "apple-touch-icon-152.png"

# Web app manifest (Windows/Android install) and a big master copy.
Save-Png (New-Icon 1024) "icon-1024.png"
Save-Png (New-Icon 512 0.84 $true) "icon-512.png"
Save-Png (New-Icon 192 0.84 $true) "icon-192.png"
Save-Png (New-Icon 512 0.64) "icon-512-maskable.png"

# Browser tab.
Save-Png (New-Icon 32 0.92 $true) "favicon-32.png"

# An .ico of PNG images in the given sizes (256 is stored as 0 in the directory, per the format).
function Save-Ico([int[]]$sizes, [string]$path) {
    $images = foreach ($size in $sizes) {
        $bmp = New-Icon $size 0.92 $true
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
    }
    $ico = New-Object System.IO.MemoryStream
    $wr = New-Object System.IO.BinaryWriter $ico
    $wr.Write([uint16]0); $wr.Write([uint16]1); $wr.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($img in $images) {
        $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
        $wr.Write([byte]$dim); $wr.Write([byte]$dim); $wr.Write([byte]0); $wr.Write([byte]0)
        $wr.Write([uint16]1); $wr.Write([uint16]32)
        $wr.Write([uint32]$img.Bytes.Length); $wr.Write([uint32]$offset)
        $offset += $img.Bytes.Length
    }
    foreach ($img in $images) { $wr.Write($img.Bytes) }
    $wr.Flush()
    [System.IO.File]::WriteAllBytes($path, $ico.ToArray())
}

# favicon.ico with 16, 32 and 48 px images, for browsers.
Save-Ico @(16, 32, 48) (Join-Path $out "..\favicon.ico")

# The Windows app's icon: every size Explorer, the taskbar and Alt+Tab ask for.
Save-Ico @(16, 20, 24, 32, 40, 48, 64, 128, 256) (Join-Path $PSScriptRoot "..\StromForbrug.Windows\app.ico")

Write-Host "Icons written to $out and StromForbrug.Windows\app.ico"
