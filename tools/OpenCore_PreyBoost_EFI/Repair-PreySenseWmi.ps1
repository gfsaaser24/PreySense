#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Deletes or arms the PreySense WMI class. Registration runs after reboot, from PreySense.
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Delete', 'Arm')]
    [string]$Action
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'System32\wbem\mofcomp.exe'
$appKey = 'HKLM:\SOFTWARE\PreySense'
$autorecoverKey = 'HKLM:\SOFTWARE\Microsoft\WBEM\CIMOM'
$autorecoverName = 'Autorecover MOFs'

function Remove-PreySenseWmiClass {
    if (-not (Test-Path -LiteralPath $compiler)) {
        throw "mofcomp.exe was not found."
    }

    $deleteMof = Join-Path $env:TEMP 'PreySenseControl-delete.mof'
    Set-Content -LiteralPath $deleteMof -Encoding ascii -Value '#pragma deleteclass("PreySenseControl", NOFAIL)'
    & $compiler '-N:root\wmi' $deleteMof
    if ($LASTEXITCODE -ne 0) {
        throw "PreySenseControl deletion failed with exit code $LASTEXITCODE."
    }
}

function Remove-PreySenseAutorecover {
    $item = Get-Item -LiteralPath $autorecoverKey -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return
    }

    $current = $item.GetValue($autorecoverName)
    if ($null -eq $current) {
        return
    }

    $entries = @($current)
    $kept = @($entries | Where-Object { $_ -notmatch 'preysense\.mof' })
    if ($kept.Count -eq $entries.Count) {
        return
    }

    New-ItemProperty -LiteralPath $autorecoverKey -Name $autorecoverName -PropertyType MultiString -Value $kept -Force | Out-Null
    Write-Host "   Removed PreySense from WMI autorecovery." -ForegroundColor Green
}

function Set-PreySenseWmiRepairPending([bool]$Pending) {
    New-Item -Path $appKey -Force | Out-Null
    if ($Pending) {
        New-ItemProperty -Path $appKey -Name WmiRepairPending -Value 1 -PropertyType DWord -Force | Out-Null
    }
    else {
        Remove-ItemProperty -Path $appKey -Name WmiRepairPending -ErrorAction SilentlyContinue
    }
}

switch ($Action) {
    'Delete' {
        Write-Host "   Deleting PreySenseControl from root\wmi..." -ForegroundColor Yellow
        Remove-PreySenseWmiClass
        Remove-PreySenseAutorecover
        Set-PreySenseWmiRepairPending $false
        Write-Host "   PreySense WMI class removed." -ForegroundColor Green
    }
    'Arm' {
        Set-PreySenseWmiRepairPending $true
        Write-Host "   WMI registration is armed for the next PreySense start after reboot." -ForegroundColor Green
    }
}
