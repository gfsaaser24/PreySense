<#
.SYNOPSIS
    Automated smoke test workflow for PreySense WPF frontend.
    Verifies process, windows, subdialogs, sliders, switches, captures screenshots, and produces a report.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Init-Tools.ps1"

$reportPath = "$PSScriptRoot\captures\smoke_test_report.md"
$subwindows = @('Settings', 'Lighting', 'Advanced', 'Metrics', 'Fans')
$testResults = [System.Collections.Generic.List[PSCustomObject]]::new()

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PreySense WPF Frontend Automated Smoke Test Suite" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Stop any running instance, compile, and launch the fresh executable.
Write-Host "`n[Step 1/5] Closing, rebuilding, and launching PreySense.Frontend..." -ForegroundColor Yellow
$main = if ($SkipBuild) {
    & "$PSScriptRoot\Restart-PreySense.ps1" -NoBuild
} else {
    & "$PSScriptRoot\Restart-PreySense.ps1"
}

if (-not $main -or -not $main.HWnd) {
    Write-Error "Failed to launch and acquire PreySense MainWindow handle."
    return
}

# 2. Capture MainWindow
Write-Host "`n[Step 2/5] Capturing MainWindow screenshot..." -ForegroundColor Yellow
$mainCap = & "$PSScriptRoot\Capture-Window.ps1" -WindowTitle "PreySense"
$mainCapPath = if ($mainCap -and $mainCap.FilePath) { $mainCap[0].FilePath } else { "" }

$testResults.Add([PSCustomObject]@{
    Component  = "MainWindow"
    Status     = "PASS"
    HWND       = $main.HWnd
    Bounds     = "$($main.Width)x$($main.Height)"
    Screenshot = $mainCapPath
    Notes      = "Process PID $($main.ProcessId) live"
})

# 3. Test Subwindows
Write-Host "`n[Step 3/5] Testing all Subwindows..." -ForegroundColor Yellow
foreach ($swName in $subwindows) {
    Write-Host "`n--- Testing Subwindow: $swName ---" -ForegroundColor White
    try {
        $sub = & "$PSScriptRoot\Open-Subwindow.ps1" -Window $swName -Capture -CloseAfter
        if ($sub -and $sub.HWnd) {
            $capFile = if ($sub.ScreenshotPath) { $sub.ScreenshotPath } else { "$PSScriptRoot\captures\latest.png" }
            $testResults.Add([PSCustomObject]@{
                Component  = $swName
                Status     = "PASS"
                HWND       = $sub.HWnd
                Bounds     = "$($sub.Width)x$($sub.Height)"
                Screenshot = $capFile
                Notes      = "Opened, verified, and closed cleanly"
            })
        } else {
            $testResults.Add([PSCustomObject]@{
                Component  = $swName
                Status     = "FAIL"
                HWND       = "N/A"
                Bounds     = "N/A"
                Screenshot = ""
                Notes      = "Window handle not acquired"
            })
        }
    } catch {
        Write-Warning "Failed subwindow test for ${swName}: $_"
        $testResults.Add([PSCustomObject]@{
            Component  = $swName
            Status     = "ERROR"
            HWND       = "N/A"
            Bounds     = "N/A"
            Screenshot = ""
            Notes      = $_.Exception.Message
        })
    }
}

# 4. Check for crash logs
Write-Host "`n[Step 4/5] Checking for crash logs..." -ForegroundColor Yellow
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$binDir = "$repoRoot\PreySense.Frontend\bin\Debug\net10.0-windows"
$crashDetected = $false
if (Test-Path "$binDir\crash.log") {
    $crashDetected = $true
    Write-Host "[!] crash.log detected!" -ForegroundColor Red
}

# 5. Output Summary Table
Write-Host "`n[Step 5/5] Smoke Test Summary:" -ForegroundColor Yellow
$testResults | Format-Table -AutoSize

# Generate Markdown Report
$sb = [System.Text.StringBuilder]::new()
$null = $sb.AppendLine("# PreySense WPF UI Automated Smoke Test Report")
$null = $sb.AppendLine("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`n")
$null = $sb.AppendLine("| Component | Status | HWND | Bounds | Notes |")
$null = $sb.AppendLine("|---|---|---|---|---|")
foreach ($r in $testResults) {
    $null = $sb.AppendLine("| **$($r.Component)** | $($r.Status) | $($r.HWND) | $($r.Bounds) | $($r.Notes) |")
}
$null = $sb.AppendLine("`n## Crash Logs Status")
$null = $sb.AppendLine("Crash Log: $(if ($crashDetected) { 'FAILED (crash.log found)' } else { 'CLEAN (0 crashes)' })")

Set-Content -Path $reportPath -Value $sb.ToString() -Encoding UTF8
Write-Host "`n[OK] Smoke test report generated at: file:///$($reportPath -replace '\\', '/')" -ForegroundColor Green

$failedResults = @($testResults | Where-Object { $_.Status -ne 'PASS' })
if ($failedResults.Count -gt 0 -or $crashDetected) {
    throw "Smoke test failed: $($failedResults.Count) UI check(s) failed and crashDetected=$crashDetected."
}
