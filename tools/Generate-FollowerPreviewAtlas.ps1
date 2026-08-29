param(
  [string]$SourcePath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Follower.png'))),
  [Parameter(Mandatory=$true)][string]$OutputPath,
  [ValidateRange(1024, 4096)][int]$Size = 4096
)
$ErrorActionPreference = 'Stop'

$sourceFullPath = [IO.Path]::GetFullPath($SourcePath)
$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $sourceFullPath -PathType Leaf)) {
  throw "Follower atlas source not found: $sourceFullPath"
}

Add-Type -AssemblyName System.Drawing
$source = [Drawing.Image]::FromFile($sourceFullPath)
try {
  if ($source.Width -ne $source.Height) {
    throw "Follower atlas must be square, actual=$($source.Width)x$($source.Height)"
  }

  $target = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
  try {
    $target.SetResolution($source.HorizontalResolution, $source.VerticalResolution)
    $graphics = [Drawing.Graphics]::FromImage($target)
    try {
      $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
      $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
      $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
      $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
      $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
      $graphics.DrawImage($source, 0, 0, $Size, $Size)
    }
    finally {
      $graphics.Dispose()
    }

    $target.Save($outputFullPath, [Drawing.Imaging.ImageFormat]::Png)
  }
  finally {
    $target.Dispose()
  }
}
finally {
  $source.Dispose()
}

$output = Get-Item -LiteralPath $outputFullPath
Write-Host "[OK] Browser preview atlas: $outputFullPath"
Write-Host "     dimensions=${Size}x${Size}, bytes=$($output.Length)"
