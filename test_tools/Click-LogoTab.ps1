Add-Type -AssemblyName UIAutomationClient
. "$PSScriptRoot\Init-Tools.ps1"

$all = [PreySense.TestTools.WindowHelper]::FindPreySenseWindows("*")
$lightWin = $null
foreach ($w in $all) {
    if ($w.Title -like "*Lighting*") {
        $lightWin = $w
        break
    }
}

if ($lightWin) {
    $hwnd = [IntPtr]$lightWin.HWnd
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $logoTab = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, "Logo"))
    if ($logoTab) {
        $sel = $null
        if ($logoTab.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) {
            ($sel -as [System.Windows.Automation.SelectionItemPattern]).Select()
            Write-Host "[OK] Selected Logo tab via SelectionItemPattern."
        }
    }

    Start-Sleep -Milliseconds 600
    $outPath = "$PSScriptRoot\captures\PreySense_Logo_Tab.png"
    $w = 0
    $h = 0
    $hBmp = [PreySense.TestTools.WindowHelper]::CaptureHBitmap($hwnd, [ref]$w, [ref]$h)
    if ($hBmp -ne [IntPtr]::Zero) {
        $img = [System.Drawing.Image]::FromHbitmap($hBmp)
        $img.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $img.Dispose()
        [PreySense.TestTools.WindowHelper]::DeleteObject($hBmp)
        Write-Host "[OK] Saved Logo tab screenshot to $outPath"
    }
} else {
    Write-Host "Lighting window not found!"
}
