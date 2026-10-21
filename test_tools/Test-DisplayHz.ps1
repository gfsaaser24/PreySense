Add-Type -Path "D:\Development\PreySense\PreySense.Core\bin\Debug\net10.0-windows\PreySense.Core.dll"
$dev = [PreySense.Helpers.DisplayManager]::GetPrimaryDeviceName()
Write-Host "Primary Dev: $dev"
$rates = [PreySense.Helpers.DisplayManager]::GetSupportedRefreshRates()
Write-Host "Supported: $($rates -join ', ')"
$cur = [PreySense.Helpers.DisplayManager]::GetCurrentRefreshRate()
Write-Host "Current: $cur Hz"

Write-Host "Calling SetRefreshRate(120)..."
$r1 = [PreySense.Helpers.DisplayManager]::SetRefreshRate(120)
Write-Host "Result 120: $r1"
$c1 = [PreySense.Helpers.DisplayManager]::GetCurrentRefreshRate()
Write-Host "Current after 120: $c1 Hz"

Start-Sleep -Milliseconds 500

Write-Host "Calling SetRefreshRate(240)..."
$r2 = [PreySense.Helpers.DisplayManager]::SetRefreshRate(240)
Write-Host "Result 240: $r2"
$c2 = [PreySense.Helpers.DisplayManager]::GetCurrentRefreshRate()
Write-Host "Current after 240: $c2 Hz"
