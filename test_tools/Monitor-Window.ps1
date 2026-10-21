<#
.SYNOPSIS
    Monitors running PreySense instances, active windows, geometry, resource usage, and crash logs.
.PARAMETER Watch
    Continuously poll every IntervalSeconds.
.PARAMETER IntervalSeconds
    Interval in seconds when in watch mode (default: 2).
#>
[CmdletBinding()]
param(
    [switch]$Watch,
    [int]$IntervalSeconds = 2
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Init-Tools.ps1"

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$binDir = "$repoRoot\PreySense.Frontend\bin\Debug\net10.0-windows"

function Show-PreySenseStatus {
    $procs = Get-Process -Name PreySense.Frontend -ErrorAction SilentlyContinue
    if (-not $procs) {
        Write-Host "[!] PreySense.Frontend is NOT running." -ForegroundColor Yellow
        Write-Host "    Start it with: .\test_tools\Restart-PreySense.ps1" -ForegroundColor Gray
        return
    }

    foreach ($proc in $procs) {
        $wsMb = [Math]::Round($proc.WorkingSet64 / 1MB, 1)
        $privMb = [Math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
        $statusColor = if ($proc.Responding) { [ConsoleColor]::Green } else { [ConsoleColor]::Red }

        Write-Host "==========================================================" -ForegroundColor Cyan
        Write-Host " PreySense Process [PID $($proc.Id)] - $(Get-Date -Format 'HH:mm:ss')" -ForegroundColor Cyan
        Write-Host "==========================================================" -ForegroundColor Cyan
        Write-Host "  Responding:    " -NoNewline
        Write-Host "$($proc.Responding)" -ForegroundColor $statusColor
        Write-Host "  Memory:        WS: ${wsMb} MB | Private: ${privMb} MB" -ForegroundColor White
        Write-Host "  Threads:       $($proc.Threads.Count)" -ForegroundColor White
        Write-Host "  CPU Time:      $($proc.TotalProcessorTime.TotalSeconds.ToString('F2')) s" -ForegroundColor White

        $windows = [PreySense.TestTools.WindowHelper]::FindWindows($proc.Id, "*") | Where-Object {
            $_.Title -ne 'CallBackWindowThread' -and $_.Width -gt 50 -and $_.Height -gt 50
        }

        Write-Host "`n  Windows ($($windows.Count) detected):" -ForegroundColor Yellow

        if ($windows.Count -eq 0) {
            Write-Host "    (No visible UI windows with positive bounds)" -ForegroundColor Gray
        } else {
            foreach ($w in $windows) {
                $visStr = if ($w.IsVisible) { "Visible" } else { "Layered/Visible" }
                $titleDisplay = if ($w.Title) { "'$($w.Title)'" } else { "(WPF Host Window)" }

                Write-Host "    HWND: $($w.HWnd) - $titleDisplay" -ForegroundColor White
                Write-Host "          State:  $visStr | Class: $($w.ClassName)" -ForegroundColor Gray
                Write-Host "          Bounds: $($w.Width)x$($w.Height) at ($($w.Left), $($w.Top))" -ForegroundColor Gray
            }
        }

        # Check for crash logs in build directory
        if (Test-Path "$binDir\crash.log") {
            Write-Host "`n  [CRITICAL] crash.log detected!" -ForegroundColor Red
            Get-Content "$binDir\crash.log" | Select-Object -First 10 | ForEach-Object {
                Write-Host "    $_" -ForegroundColor Red
            }
        }
        if (Test-Path "$binDir\crash_domain.log") {
            Write-Host "`n  [CRITICAL] crash_domain.log detected!" -ForegroundColor Red
            Get-Content "$binDir\crash_domain.log" | Select-Object -First 10 | ForEach-Object {
                Write-Host "    $_" -ForegroundColor Red
            }
        }
    }
}

if ($Watch) {
    Write-Host "Watching PreySense every $IntervalSeconds second(s)... (Press Ctrl+C to stop)`n" -ForegroundColor Gray
    while ($true) {
        Clear-Host
        Show-PreySenseStatus
        Start-Sleep -Seconds $IntervalSeconds
    }
} else {
    Show-PreySenseStatus
}
