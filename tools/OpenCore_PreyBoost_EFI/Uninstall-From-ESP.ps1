<#
.SYNOPSIS
    Removes the hidden PreySense OpenCore boot partition.
#>
#Requires -RunAsAdministrator

$ErrorActionPreference = 'Stop'
$partitionLabel = 'PREYSENSE'
$efiPartitionType = [guid]'C12A7328-F81F-11D2-BA4B-00A0C93EC93B'
$legacyPartitionType = [guid]'C0DE0001-5052-4559-534E-4F4330310001'

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PreySense OpenCore Uninstaller" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

Write-Host "`n1. Removing the hidden PreySense partition..." -ForegroundColor Yellow
$parts = @()
foreach ($volume in Get-Volume | Where-Object { $_.FileSystemLabel -eq $partitionLabel }) {
    if ($volume.DriveLetter) {
        $parts += Get-Partition -DriveLetter $volume.DriveLetter
    } else {
        $parts += Get-Partition | Where-Object { $_.AccessPaths -contains $volume.Path }
    }
}
if ($parts.Count -eq 0) {
    $parts = @(Get-Partition | Where-Object {
        try {
            $type = [guid]$_.GptType
            $type -eq $legacyPartitionType -or
            ($type -eq $efiPartitionType -and $_.Size -le 32MB -and $_.Size -ge 8MB)
        } catch { $false }
    })
}

if ($parts.Count -eq 0) {
    Write-Host "   No PreySense partition found." -ForegroundColor Green
} else {
    $sysLetter = $env:SystemDrive.TrimEnd('\').Substring(0, 1)
    $windows = Get-Partition -DriveLetter $sysLetter
    foreach ($part in $parts) {
        if ($part.DriveLetter) {
            Remove-PartitionAccessPath -DriveLetter $part.DriveLetter -AccessPath "$($part.DriveLetter):\" -ErrorAction SilentlyContinue
        }
        Write-Host "   Deleting disk $($part.DiskNumber) partition $($part.PartitionNumber)..." -ForegroundColor Yellow
        Remove-Partition -DiskNumber $part.DiskNumber -PartitionNumber $part.PartitionNumber -Confirm:$false
        Write-Host "   Partition removed." -ForegroundColor Green
    }

    try {
        $windows = Get-Partition -DriveLetter $sysLetter
        $supported = Get-PartitionSupportedSize -DiskNumber $windows.DiskNumber -PartitionNumber $windows.PartitionNumber
        if ($supported.SizeMax -gt $windows.Size) {
            Resize-Partition -DiskNumber $windows.DiskNumber -PartitionNumber $windows.PartitionNumber -Size $supported.SizeMax
            Write-Host "   Reclaimed free space into the Windows partition." -ForegroundColor Green
        }
    } catch {
        Write-Host "   Partition deleted. Windows could not be expanded automatically: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

Write-Host "`n[SUCCESS] OpenCore uninstalled. Firmware should boot Windows Boot Manager again." -ForegroundColor Green
