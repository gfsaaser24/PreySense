Add-Type -Path "D:\Development\PreySense\PreySense.Core\bin\Debug\net10.0-windows\PreySense.Core.dll"
$res = [PreySense.Helpers.DisplayManager]::SetRefreshRate(120)
$cur = [PreySense.Helpers.DisplayManager]::GetCurrentRefreshRate()
Set-Content "D:\Development\PreySense\test_tools\hz_result.txt" "Result: $res, Current: $cur Hz"
