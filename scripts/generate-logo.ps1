# Generates the Agent Island app logo at multiple sizes.
# Outputs:
#   assets/icon.ico     -- Windows multi-resolution icon (16, 24, 32, 48, 64, 128, 256), embedded in the .exe
#   assets/logo-256.png -- README hero / web use
#   assets/logo-1024.png -- High-res master

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $repoRoot "assets"
if (!(Test-Path $assets)) { New-Item -ItemType Directory -Path $assets | Out-Null }

function New-RoundedPath {
    param([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $width - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $width - $d, $y + $height - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $height - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-AgentIslandLogo {
    param([int]$size)

    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $u = [single]($size / 256.0)
    $cx = [single](128 * $u)
    $cy = [single](128 * $u)

    # ---- Dark rounded square background (the island body) ----
    $insetEdge = [single](16 * $u)
    $bgX = $insetEdge
    $bgY = $insetEdge
    $bgW = [single]($size - $insetEdge * 2)
    $bgH = [single]($size - $insetEdge * 2)
    $bgRadius = [single](56 * $u)
    if ($bgRadius -gt ($bgW / 2)) { $bgRadius = $bgW / 2 }

    $bgPath = New-RoundedPath -x $bgX -y $bgY -width $bgW -height $bgH -radius $bgRadius

    $bgBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 12, 16, 24))
    $g.FillPath($bgBrush, $bgPath)

    # Subtle aurora highlight on the upper-right (only at larger sizes)
    if ($size -ge 64) {
        $auroraPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $auroraRadiusX = [single]($bgW * 0.55)
        $auroraRadiusY = [single]($bgH * 0.62)
        $auroraCenterX = [single]($bgX + $bgW * 0.72)
        $auroraCenterY = [single]($bgY + $bgH * 0.46)
        $auroraPath.AddEllipse(
            $auroraCenterX - $auroraRadiusX,
            $auroraCenterY - $auroraRadiusY,
            $auroraRadiusX * 2,
            $auroraRadiusY * 2)
        $auroraBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush($auroraPath)
        $auroraBrush.CenterColor = [System.Drawing.Color]::FromArgb(70, 54, 211, 153)
        $auroraBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 54, 211, 153))
        $auroraBrush.CenterPoint = New-Object System.Drawing.PointF($auroraCenterX, $auroraCenterY)
        $clip = $g.Clip
        $g.SetClip($bgPath, [System.Drawing.Drawing2D.CombineMode]::Replace)
        $g.FillPath($auroraBrush, $auroraPath)
        $g.Clip = $clip
        $auroraBrush.Dispose()
        $auroraPath.Dispose()
    }

    # Subtle 1-pixel rim
    $rimAlpha = if ($size -ge 32) { 60 } else { 0 }
    if ($rimAlpha -gt 0) {
        $rimPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb($rimAlpha, 255, 255, 255), [single](1.4 * $u))
        $g.DrawPath($rimPen, $bgPath)
        $rimPen.Dispose()
    }

    # ---- Pulse rings (only at larger sizes) ----
    if ($size -ge 64) {
        $ringR1 = [single](68 * $u)
        $ringR2 = [single](54 * $u)
        $pen1 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(36, 54, 211, 153), [single](1.6 * $u))
        $pen2 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(76, 54, 211, 153), [single](1.8 * $u))
        $g.DrawEllipse($pen1, ($cx - $ringR1), ($cy - $ringR1), ($ringR1 * 2), ($ringR1 * 2))
        $g.DrawEllipse($pen2, ($cx - $ringR2), ($cy - $ringR2), ($ringR2 * 2), ($ringR2 * 2))
        $pen1.Dispose()
        $pen2.Dispose()
    }

    # ---- Soft green halo behind the dot (only at >= 48) ----
    if ($size -ge 48) {
        $haloPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $haloR = [single](58 * $u)
        $haloPath.AddEllipse(($cx - $haloR), ($cy - $haloR), ($haloR * 2), ($haloR * 2))
        $haloBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush($haloPath)
        $haloBrush.CenterColor = [System.Drawing.Color]::FromArgb(130, 54, 211, 153)
        $haloBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 54, 211, 153))
        $haloBrush.CenterPoint = New-Object System.Drawing.PointF($cx, $cy)
        $g.FillPath($haloBrush, $haloPath)
        $haloBrush.Dispose()
        $haloPath.Dispose()
    }

    # ---- The green signal dot ----
    $dotR = if ($size -le 24) { [single](44 * $u) } else { [single](38 * $u) }
    $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 54, 211, 153))
    $g.FillEllipse($dotBrush, ($cx - $dotR), ($cy - $dotR), ($dotR * 2), ($dotR * 2))
    $dotBrush.Dispose()

    # ---- Specular highlight on the dot (only at larger sizes) ----
    if ($size -ge 48) {
        $hlPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $hlR = [single]($dotR * 0.70)
        $hlCx = [single]($cx - $dotR * 0.18)
        $hlCy = [single]($cy - $dotR * 0.28)
        $hlPath.AddEllipse(($hlCx - $hlR), ($hlCy - $hlR), ($hlR * 2), ($hlR * 2))
        $hlBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush($hlPath)
        $hlBrush.CenterColor = [System.Drawing.Color]::FromArgb(150, 188, 252, 220)
        $hlBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 188, 252, 220))
        $hlBrush.CenterPoint = New-Object System.Drawing.PointF($hlCx, $hlCy)
        $g.FillPath($hlBrush, $hlPath)
        $hlBrush.Dispose()
        $hlPath.Dispose()
    }

    $bgBrush.Dispose()
    $bgPath.Dispose()
    $g.Dispose()

    return $bmp
}

function Save-PngBytes {
    param([System.Drawing.Bitmap]$bmp)
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

function Build-IcoFile {
    param([int[]]$sizes, [string]$outPath)

    $imageBytes = @{}
    foreach ($s in $sizes) {
        $bmp = New-AgentIslandLogo -size $s
        $imageBytes[$s] = Save-PngBytes -bmp $bmp
        $bmp.Dispose()
    }

    if (Test-Path $outPath) { Remove-Item -LiteralPath $outPath -Force }
    $stream = [System.IO.File]::Open($outPath, [System.IO.FileMode]::Create)
    $writer = New-Object System.IO.BinaryWriter($stream)

    # ICO header
    $writer.Write([uint16]0)             # reserved
    $writer.Write([uint16]1)             # type = icon
    $writer.Write([uint16]$sizes.Count)  # count

    # Directory entries
    $headerSize = 6 + (16 * $sizes.Count)
    $offset = [uint32]$headerSize
    foreach ($s in $sizes) {
        $bytes = $imageBytes[$s]
        $w = if ($s -ge 256) { [byte]0 } else { [byte]$s }
        $h = if ($s -ge 256) { [byte]0 } else { [byte]$s }
        $writer.Write($w)                # width
        $writer.Write($h)                # height
        $writer.Write([byte]0)           # palette count
        $writer.Write([byte]0)           # reserved
        $writer.Write([uint16]1)         # planes
        $writer.Write([uint16]32)        # bits per pixel
        $writer.Write([uint32]$bytes.Length)
        $writer.Write([uint32]$offset)
        $offset = [uint32]($offset + $bytes.Length)
    }

    # Image bytes
    foreach ($s in $sizes) {
        $writer.Write($imageBytes[$s])
    }

    $writer.Close()
    $stream.Close()

    Write-Host "Wrote $outPath"
}

function Save-PngFile {
    param([int]$size, [string]$path)
    $bmp = New-AgentIslandLogo -size $size
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Wrote $path"
}

# ---- Run ----
$icoPath = Join-Path $assets "icon.ico"
$png256Path = Join-Path $assets "logo-256.png"
$png1024Path = Join-Path $assets "logo-1024.png"

Build-IcoFile -sizes @(16, 24, 32, 48, 64, 128, 256) -outPath $icoPath
Save-PngFile -size 256 -path $png256Path
Save-PngFile -size 1024 -path $png1024Path
