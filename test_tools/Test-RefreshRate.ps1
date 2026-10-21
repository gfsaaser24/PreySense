Add-Type -Path "D:\Development\PreySense\PreySense.Core\bin\Release\net10.0-windows\PreySense.Core.dll"

$cur = [PreySense.Core.Display.DisplayManager]::GetCurrentRefreshRate()
Write-Host "Initial Refresh Rate: $cur Hz"

Write-Host "Attempting switch to 120 Hz..."
$res120 = [PreySense.Core.Display.DisplayManager]::SetRefreshRate(120)
Write-Host "Switch to 120 Hz success: $res120"

Start-Sleep -Seconds 1
$after120 = [PreySense.Core.Display.DisplayManager]::GetCurrentRefreshRate()
Write-Host "Active Refresh Rate: $after120 Hz"

Write-Host "Attempting switch back to 240 Hz..."
$res240 = [PreySense.Core.Display.DisplayManager]::SetRefreshRate(240)
Write-Host "Switch back to 240 Hz success: $res240"

Start-Sleep -Seconds 1
$after240 = [PreySense.Core.Display.DisplayManager]::GetCurrentRefreshRate()
Write-Host "Final Active Refresh Rate: $after240 Hz"
