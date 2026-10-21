<#
.SYNOPSIS
    Rebuilds and launches PreySense.Frontend directly on the user's interactive desktop (WinSta0\Default).
.PARAMETER NoBuild
    Skip building the solution and just restart the application.
.PARAMETER Config
    Build configuration: 'Debug' (default) or 'Release'.
.PARAMETER TimeoutSeconds
    Maximum seconds to wait for window initialization (default: 8).
#>
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [string]$Config = 'Debug',
    [int]$TimeoutSeconds = 8
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Init-Tools.ps1"

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path

Write-Host "=== PreySense Hot-Restart Tool ===" -ForegroundColor Cyan

# 1. Stop every running frontend before compiling. This prevents the executable
# from being locked and guarantees that the test launches the newly built code.
$runningProcesses = @(Get-Process -Name PreySense.Frontend -ErrorAction SilentlyContinue)
if ($runningProcesses.Count -gt 0) {
    Write-Host "[*] Closing $($runningProcesses.Count) running PreySense.Frontend instance(s)..." -ForegroundColor Gray
    $runningProcesses | Stop-Process -Force -ErrorAction Stop

    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $remainingProcesses = @(Get-Process -Name PreySense.Frontend -ErrorAction SilentlyContinue)
        if ($remainingProcesses.Count -eq 0) {
            break
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($remainingProcesses.Count -gt 0) {
        $remainingIds = ($remainingProcesses.Id -join ', ')
        throw "Unable to stop PreySense.Frontend process(es): $remainingIds"
    }

    Write-Host "[OK] All running PreySense.Frontend instances are closed." -ForegroundColor Yellow
}

# 2. Clean old crash logs in bin directory
$binDir = "$repoRoot\PreySense.Frontend\bin\$Config\net10.0-windows"
if (Test-Path $binDir) {
    Get-ChildItem -Path $binDir -Filter "crash*.log" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
}

# 3. Build solution if requested. Test-UiWorkflow invokes this by default before
# performing any UI automation; -NoBuild is an explicit opt-out only.
if (-not $NoBuild) {
    Write-Host "[*] Building PreySense.slnx ($Config)..." -ForegroundColor Gray
    $buildOutput = & dotnet build "$repoRoot\PreySense.slnx" -c $Config --nologo
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $msg = $buildOutput -join [Environment]::NewLine
        Write-Error "Build failed with exit code $exitCode`n$msg"
        return
    }
    Write-Host "[OK] Build succeeded." -ForegroundColor Green
}

# 4. Check executable
$exePath = "$binDir\PreySense.Frontend.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found at: $exePath"
    return
}

# 5. Launch process on interactive desktop (WinSta0\Default)
Write-Host "[*] Launching $exePath on interactive desktop..." -ForegroundColor Gray
$launchedPid = [PreySense.TestTools.WindowHelper]::LaunchOnInteractiveDesktop($exePath, "")

# 6. Wait for window to initialize
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$foundWindow = $null

while ($sw.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    $proc = Get-Process -Id $launchedPid -ErrorAction SilentlyContinue
    if (-not $proc -or $proc.HasExited) {
        Write-Error "Process exited unexpectedly!"
        if (Test-Path "$binDir\crash.log") {
            Write-Host "--- crash.log ---" -ForegroundColor Red
            Get-Content "$binDir\crash.log"
        }
        return
    }

    $wins = [PreySense.TestTools.WindowHelper]::FindWindows($launchedPid, "*")
    if ($wins.Count -gt 0) {
        $foundWindow = $wins[0]
        break
    }
    Start-Sleep -Milliseconds 250
}

if ($foundWindow) {
    # A WPF HWND can exist before the visual and UI Automation trees are ready.
    # Wait for a known main-window control so captures and clicks never race the
    # initial black/unrendered surface.
    $automationReady = $false
    $settingsCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        'BtnSettings')
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        $mainElement = [System.Windows.Automation.AutomationElement]::FromHandle($foundWindow.HWnd)
        if ($mainElement -and $mainElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $settingsCondition)) {
            $automationReady = $true
            break
        }
        Start-Sleep -Milliseconds 100
    }

    if (-not $automationReady) {
        Stop-Process -Id $launchedPid -Force -ErrorAction SilentlyContinue
        throw "PreySense created a window but its UI did not become automation-ready within $TimeoutSeconds seconds."
    }

    # Refresh bounds after WPF completes its auto-size layout pass.
    $foundWindow = ([PreySense.TestTools.WindowHelper]::FindWindows($launchedPid, "*") | Where-Object { $_.Title -eq 'PreySense' } | Select-Object -First 1)

    Write-Host "[OK] PreySense window is LIVE on Interactive Desktop!" -ForegroundColor Green
    Write-Host "     PID:     $($foundWindow.ProcessId)" -ForegroundColor White
    Write-Host "     HWND:    $($foundWindow.HWnd)" -ForegroundColor White
    Write-Host "     Title:   '$($foundWindow.Title)'" -ForegroundColor White
    Write-Host "     Class:   $($foundWindow.ClassName)" -ForegroundColor Gray
    Write-Host "     Bounds:  $($foundWindow.Width)x$($foundWindow.Height) at ($($foundWindow.Left), $($foundWindow.Top))" -ForegroundColor White

    return [PSCustomObject]@{
        ProcessId = $foundWindow.ProcessId
        HWnd      = $foundWindow.HWnd
        Title     = $foundWindow.Title
        Width     = $foundWindow.Width
        Height    = $foundWindow.Height
        Left      = $foundWindow.Left
        Top       = $foundWindow.Top
        ClassName = $foundWindow.ClassName
    }
} else {
    Write-Warning "Process started (PID: $launchedPid) but window handle could not be confirmed within $TimeoutSeconds seconds."
}
