Add-Type -AssemblyName UIAutomationClient
. "$PSScriptRoot\Init-Tools.ps1"

$all = [PreySense.TestTools.WindowHelper]::FindPreySenseWindows("PreySense")
$mainWin = $null
foreach ($w in $all) {
    if ($w.Title -eq "PreySense") {
        $mainWin = $w
        break
    }
}

if (-not $mainWin) {
    Write-Error "MainWindow not found!"
    exit 1
}

$hwnd = [IntPtr]$mainWin.HWnd
$el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

Write-Host "Looking for BtnRefresh120..."
$btn120 = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "BtnRefresh120"))

if ($btn120) {
    Write-Host "Found BtnRefresh120! Invoking click..."
    $inv = $null
    if ($btn120.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) {
        ($inv -as [System.Windows.Automation.InvokePattern]).Invoke()
        Write-Host "[OK] Clicked 120 Hz button!"
    } else {
        Write-Warning "Could not get InvokePattern on BtnRefresh120"
    }

    Start-Sleep -Seconds 2

    # Capture after 120Hz click
    $out120 = "$PSScriptRoot\captures\PreySense_Refresh_120Hz.png"
    $w = 0; $h = 0
    $hBmp = [PreySense.TestTools.WindowHelper]::CaptureHBitmap($hwnd, [ref]$w, [ref]$h)
    if ($hBmp -ne [IntPtr]::Zero) {
        $img = [System.Drawing.Image]::FromHbitmap($hBmp)
        $img.Save($out120, [System.Drawing.Imaging.ImageFormat]::Png)
        $img.Dispose()
        [PreySense.TestTools.WindowHelper]::DeleteObject($hBmp)
        Write-Host "[OK] Saved 120Hz screenshot to $out120"
    }

    # Now click 240 Hz button
    $btn240 = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "BtnRefresh240"))
    if ($btn240) {
        Write-Host "Found BtnRefresh240! Invoking click..."
        $inv240 = $null
        if ($btn240.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv240)) {
            ($inv240 -as [System.Windows.Automation.InvokePattern]).Invoke()
            Write-Host "[OK] Clicked 240 Hz button!"
        }
        Start-Sleep -Seconds 2
        $out240 = "$PSScriptRoot\captures\PreySense_Refresh_240Hz.png"
        $hBmp = [PreySense.TestTools.WindowHelper]::CaptureHBitmap($hwnd, [ref]$w, [ref]$h)
        if ($hBmp -ne [IntPtr]::Zero) {
            $img = [System.Drawing.Image]::FromHbitmap($hBmp)
            $img.Save($out240, [System.Drawing.Imaging.ImageFormat]::Png)
            $img.Dispose()
            [PreySense.TestTools.WindowHelper]::DeleteObject($hBmp)
            Write-Host "[OK] Saved 240Hz screenshot to $out240"
        }
    }
} else {
    Write-Error "BtnRefresh120 not found on MainWindow!"
}
