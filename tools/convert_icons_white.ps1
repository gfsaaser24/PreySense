Add-Type -AssemblyName System.Drawing

$resDir = ".\PreySense.Frontend\Resources"
$pngFiles = Get-ChildItem -Path $resDir -Filter "*.png"

Write-Host "Converting $($pngFiles.Count) icons to pure white for dark theme..."

foreach ($file in $pngFiles) {
    $bmp = [System.Drawing.Bitmap]::new($file.FullName)
    $newBmp = [System.Drawing.Bitmap]::new($bmp.Width, $bmp.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    for ($x = 0; $x -lt $bmp.Width; $x++) {
        for ($y = 0; $y -lt $bmp.Height; $y++) {
            $pixel = $bmp.GetPixel($x, $y)
            if ($pixel.A -gt 0) {
                # Preserve the original alpha transparency channel, convert RGB to crisp white (240, 240, 240)
                $whitePixel = [System.Drawing.Color]::FromArgb($pixel.A, 240, 240, 240)
                $newBmp.SetPixel($x, $y, $whitePixel)
            } else {
                $newBmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
            }
        }
    }

    $bmp.Dispose()
    # Save back as PNG
    $newBmp.Save($file.FullName, [System.Drawing.Imaging.ImageFormat]::Png)
    $newBmp.Dispose()
}

Write-Host "All icons converted to white successfully."
