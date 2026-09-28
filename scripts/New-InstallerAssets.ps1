[CmdletBinding()]
param(
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'installer\assets'
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

function New-Canvas([int] $width, [int] $height) {
    $bitmap = [System.Drawing.Bitmap]::new($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(144, 144)
    return $bitmap
}

function Set-Quality([System.Drawing.Graphics] $graphics) {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
}

function Draw-ContainedImage(
    [System.Drawing.Graphics] $graphics,
    [System.Drawing.Image] $image,
    [System.Drawing.RectangleF] $bounds
) {
    $scale = [Math]::Min($bounds.Width / $image.Width, $bounds.Height / $image.Height)
    $width = [single]($image.Width * $scale)
    $height = [single]($image.Height * $scale)
    $x = [single]($bounds.X + (($bounds.Width - $width) / 2))
    $y = [single]($bounds.Y + (($bounds.Height - $height) / 2))
    $graphics.DrawImage($image, $x, $y, $width, $height)
}

$logoPath = Join-Path $repositoryRoot 'assets\logo-fire.png'
$mascotPath = Join-Path $repositoryRoot 'assets\ghost_menu.png'
$logo = [System.Drawing.Image]::FromFile($logoPath)
$mascot = [System.Drawing.Image]::FromFile($mascotPath)

try {
    $large = New-Canvas 500 960
    $graphics = [System.Drawing.Graphics]::FromImage($large)
    try {
        Set-Quality $graphics
        $background = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.Rectangle]::new(0, 0, 500, 960),
            [System.Drawing.Color]::FromArgb(255, 2, 10, 29),
            [System.Drawing.Color]::FromArgb(255, 7, 55, 105),
            90
        )
        try { $graphics.FillRectangle($background, 0, 0, 500, 960) } finally { $background.Dispose() }

        $cyanGlow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(30, 31, 210, 255))
        $blueGlow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(42, 28, 91, 220))
        try {
            $graphics.FillEllipse($cyanGlow, -130, 45, 540, 540)
            $graphics.FillEllipse($blueGlow, 160, 400, 520, 520)
            $graphics.FillEllipse($cyanGlow, -180, 690, 450, 360)
        }
        finally {
            $cyanGlow.Dispose()
            $blueGlow.Dispose()
        }

        Draw-ContainedImage $graphics $logo ([System.Drawing.RectangleF]::new(28, 35, 444, 350))
        Draw-ContainedImage $graphics $mascot ([System.Drawing.RectangleF]::new(38, 360, 424, 430))

        $titleFont = [System.Drawing.Font]::new('Segoe UI', 23, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $bodyFont = [System.Drawing.Font]::new('Segoe UI', 17, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
        $whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
        $cyanBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 112, 226, 255))
        $center = [System.Drawing.StringFormat]::new()
        $center.Alignment = [System.Drawing.StringAlignment]::Center
        try {
            $graphics.DrawString('Sua biblioteca. Seu jeito.', $titleFont, $whiteBrush, [System.Drawing.RectangleF]::new(22, 790, 456, 42), $center)
            $bullet = [char]0x2022
            $accentedO = [char]0x00F3
            $tagline = "Organize $bullet Assista $bullet Viva cada hist${accentedO}ria"
            $graphics.DrawString($tagline, $bodyFont, $cyanBrush, [System.Drawing.RectangleF]::new(18, 842, 464, 50), $center)
        }
        finally {
            $titleFont.Dispose(); $bodyFont.Dispose(); $whiteBrush.Dispose(); $cyanBrush.Dispose(); $center.Dispose()
        }

        $large.Save((Join-Path $resolvedOutput 'wizard-large.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $large.Dispose()
    }

    $small = New-Canvas 512 512
    $graphics = [System.Drawing.Graphics]::FromImage($small)
    try {
        Set-Quality $graphics
        $background = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.Rectangle]::new(0, 0, 512, 512),
            [System.Drawing.Color]::FromArgb(255, 4, 19, 48),
            [System.Drawing.Color]::FromArgb(255, 12, 94, 164),
            45
        )
        try { $graphics.FillRectangle($background, 0, 0, 512, 512) } finally { $background.Dispose() }
        $glow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(42, 63, 220, 255))
        try { $graphics.FillEllipse($glow, 16, 16, 480, 480) } finally { $glow.Dispose() }
        Draw-ContainedImage $graphics $logo ([System.Drawing.RectangleF]::new(34, 34, 444, 444))
        $small.Save((Join-Path $resolvedOutput 'wizard-small.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $small.Dispose()
    }
}
finally {
    $logo.Dispose()
    $mascot.Dispose()
}

Write-Output "Artes do instalador geradas em $resolvedOutput"
