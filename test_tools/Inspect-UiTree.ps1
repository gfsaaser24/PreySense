<#
.SYNOPSIS
    Inspects and dumps UI controls, sliders, switches, buttons, and text in PreySense using UI Automation.
.PARAMETER WindowTitle
    Filter for specific window (default: '*' for all UI windows).
.PARAMETER Sliders
    Filter to sliders and their range values.
.PARAMETER Switches
    Filter to toggle switches / checkboxes and their states.
.PARAMETER Buttons
    Filter to buttons and their enabled states.
.PARAMETER Text
    Filter to text blocks / labels.
.PARAMETER Query
    Search elements by Name or AutomationId substring.
#>
[CmdletBinding()]
param(
    [string]$WindowTitle = "*",
    [switch]$Sliders,
    [switch]$Switches,
    [switch]$Buttons,
    [switch]$Text,
    [string]$Query
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Init-Tools.ps1"

$proc = Get-Process -Name PreySense.Frontend -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) {
    Write-Warning "PreySense.Frontend is not running. Launch it with: .\test_tools\Restart-PreySense.ps1"
    return
}

$wins = [PreySense.TestTools.WindowHelper]::FindWindows($proc.Id, $WindowTitle) | Where-Object {
    $_.Title -ne 'CallBackWindowThread' -and $_.Width -gt 50 -and $_.Height -gt 50
}

if ($wins.Count -eq 0) {
    Write-Warning "No UI windows found for PID $($proc.Id)."
    return
}

Write-Host "=== PreySense UI Inspector (PID $($proc.Id)) ===" -ForegroundColor Cyan

foreach ($win in $wins) {
    $titleDisplay = if ($win.Title) { "'$($win.Title)'" } else { "(WPF Host Window)" }
    Write-Host "`n[Window] $titleDisplay (HWND: $($win.HWnd)) Bounds: $($win.Width)x$($win.Height) at ($($win.Left), $($win.Top))" -ForegroundColor Green

    $ae = $null
    try {
        $ae = [System.Windows.Automation.AutomationElement]::FromHandle($win.HWnd)
    } catch {
        Write-Warning "Failed to acquire AutomationElement for HWND $($win.HWnd): $_"
        continue
    }

    if (-not $ae) { continue }

    $descendants = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "  Found $($descendants.Count) elements in visual tree." -ForegroundColor Gray

    $results = [System.Collections.Generic.List[PSCustomObject]]::new()

    foreach ($el in $descendants) {
        $type = $el.Current.ControlType.ProgrammaticName.Replace("ControlType.", "")
        $name = $el.Current.Name
        $autoId = $el.Current.AutomationId
        $enabled = $el.Current.IsEnabled
        $val = ""

        # Check RangeValuePattern (Sliders)
        $rangeObj = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern, [ref]$rangeObj)) {
            $rangePattern = $rangeObj -as [System.Windows.Automation.RangeValuePattern]
            if ($rangePattern) {
                $val = "Val: $($rangePattern.Current.Value) [Min: $($rangePattern.Current.Minimum), Max: $($rangePattern.Current.Maximum)]"
            }
        }

        # Check TogglePattern (Switches)
        $toggleObj = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$toggleObj)) {
            $togglePattern = $toggleObj -as [System.Windows.Automation.TogglePattern]
            if ($togglePattern) {
                $val = "Toggle: $($togglePattern.Current.ToggleState)"
            }
        }

        # Check ValuePattern (Inputs/Text)
        $valObj = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valObj)) {
            $valPattern = $valObj -as [System.Windows.Automation.ValuePattern]
            if ($valPattern -and -not $val) {
                $val = "Text: '$($valPattern.Current.Value)'"
            }
        }

        # Filter logic
        $include = $true
        if ($Sliders -and $type -ne 'Slider') { $include = $false }
        if ($Switches -and $type -notin @('CheckBox', 'RadioButton', 'Custom')) { $include = $false }
        if ($Buttons -and $type -ne 'Button') { $include = $false }
        if ($Text -and $type -ne 'Text') { $include = $false }
        if ($Query -and ($name -notlike "*$Query*" -and $autoId -notlike "*$Query*")) { $include = $false }

        if ($include) {
            $results.Add([PSCustomObject]@{
                Type         = $type
                Name         = if ($name.Length -gt 40) { $name.Substring(0, 37) + "..." } else { $name }
                AutomationId = $autoId
                StateValue   = $val
                Enabled      = $enabled
            })
        }
    }

    if ($results.Count -gt 0) {
        $results | Format-Table -AutoSize
    } else {
        Write-Host "  (No elements matched the specified filter)" -ForegroundColor DarkGray
    }
}
