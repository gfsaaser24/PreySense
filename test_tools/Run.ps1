<#
.SYNOPSIS
    Central command dispatcher for PreySense developer & prototyping test tools.
.EXAMPLE
    .\test_tools\Run.ps1 -Restart
    .\test_tools\Run.ps1 -Monitor
    .\test_tools\Run.ps1 -Capture
    .\test_tools\Run.ps1 -Inspect -Sliders
    .\test_tools\Run.ps1 -Open Lighting
    .\test_tools\Run.ps1 -SmokeTest
#>
[CmdletBinding()]
param(
    [switch]$Restart,
    [switch]$Monitor,
    [switch]$Capture,
    [switch]$Inspect,
    [string]$Open,
    [switch]$SmokeTest,

    # Pass-through options
    [switch]$Watch,
    [switch]$Sliders,
    [switch]$Switches,
    [switch]$Buttons,
    [switch]$Text,
    [string]$Query,
    [string]$WindowTitle = "PreySense",
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

if ($Restart) {
    if ($NoBuild) {
        & "$PSScriptRoot\Restart-PreySense.ps1" -NoBuild
    } else {
        & "$PSScriptRoot\Restart-PreySense.ps1"
    }
}
elseif ($Monitor) {
    if ($Watch) {
        & "$PSScriptRoot\Monitor-Window.ps1" -Watch
    } else {
        & "$PSScriptRoot\Monitor-Window.ps1"
    }
}
elseif ($Capture) {
    & "$PSScriptRoot\Capture-Window.ps1" -WindowTitle $WindowTitle
}
elseif ($Inspect) {
    $inspectArgs = @{ WindowTitle = $WindowTitle }
    if ($Sliders) { $inspectArgs['Sliders'] = $true }
    if ($Switches) { $inspectArgs['Switches'] = $true }
    if ($Buttons) { $inspectArgs['Buttons'] = $true }
    if ($Text) { $inspectArgs['Text'] = $true }
    if ($Query) { $inspectArgs['Query'] = $Query }
    & "$PSScriptRoot\Inspect-UiTree.ps1" @inspectArgs
}
elseif ($Open) {
    & "$PSScriptRoot\Open-Subwindow.ps1" -Window $Open -Capture -Inspect
}
elseif ($SmokeTest) {
    if ($NoBuild) {
        & "$PSScriptRoot\Test-UiWorkflow.ps1" -SkipBuild
    } else {
        & "$PSScriptRoot\Test-UiWorkflow.ps1"
    }
}
else {
    Write-Host "=== PreySense Test & Rapid Prototyping Tools ===" -ForegroundColor Cyan
    Write-Host "Usage:" -ForegroundColor Yellow
    Write-Host "  .\test_tools\Run.ps1 -Restart                # Rebuild & launch on interactive desktop"
    Write-Host "  .\test_tools\Run.ps1 -Monitor                # Check process, memory, HWND, and crash logs"
    Write-Host "  .\test_tools\Run.ps1 -Capture                # Take high-res screenshot of PreySense"
    Write-Host "  .\test_tools\Run.ps1 -Inspect [-Sliders]     # Inspect UI Automation visual tree"
    Write-Host "  .\test_tools\Run.ps1 -Open [Lighting|Fans|.] # Programmatically open and test subwindow"
    Write-Host "  .\test_tools\Run.ps1 -SmokeTest              # Run automated end-to-end smoke test suite"
}
