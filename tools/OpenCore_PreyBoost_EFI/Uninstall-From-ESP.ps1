<#
.SYNOPSIS
    Removes the PreySense WMI class and the hidden PREYSENSE boot partition.

.DESCRIPTION
    Deletes only the PreySenseControl WMI class and its autorecovery entry,
    using Repair-PreySenseWmi.ps1 -Action Delete. Then removes the specifically
    identified PREYSENSE partition. It does not reset the WMI repository.

    IMPORTANT:
      - Running this script deletes PreySense's OpenCore partition.
      - It does not remove Windows or unrelated EFI partitions.
      - The currently loaded SSDT remains in memory until the next reboot.
      - After reboot, select Windows Boot Manager in UEFI if necessary.

.EXAMPLE
    .\Uninstall-From-ESP.ps1
    Run in an elevated Windows PowerShell session, from the packaged folder.

.NOTES
    Requires Repair-PreySenseWmi.ps1 beside this script.
    Back up important data before changing disk partitions.
#>
#Requires -RunAsAdministrator

$ErrorActionPreference = 'Stop'

# -----------------------------------------------------------------------------
# Configuration
# -----------------------------------------------------------------------------
$partitionLabel      = 'PREYSENSE'
$wmiHelper           = Join-Path $PSScriptRoot 'Repair-PreySenseWmi.ps1'
$efiPartitionType    = [guid]'C12A7328-F81F-11D2-BA4B-00A0C93EC93B'
$legacyPartitionType = [guid]'C0DE0001-5052-4559-534E-4F4330310001'

function Write-Step {
    param([int]$Number, [string]$Description)
    Write-Host ""
    Write-Host ("[{0}/2] {1}" -f $Number, $Description) -ForegroundColor Cyan
}

function Write-Info {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor White
}

function Get-WindowsPartition {
    $driveLetter = $env:SystemDrive.Substring(0, 1)
    Get-Partition -DriveLetter $driveLetter -ErrorAction Stop
}

function Get-PreySensePartition {
    # Only a PREYSENSE-labeled EFI partition or PreySense's unique legacy GPT
    # type may be removed. Never fall back to 'any small EFI partition'.
    $windows = Get-WindowsPartition
    $candidates = @()

    foreach ($volume in @(Get-Volume | Where-Object { $_.FileSystemLabel -eq $partitionLabel })) {
        $part = Get-Partition -Volume $volume -ErrorAction SilentlyContinue |
            Select-Object -First 1

        if (-not $part) {
            throw 'Found PREYSENSE volume, but could not identify its partition. Uninstall stopped.'
        }
        if ($part.DiskNumber -ne $windows.DiskNumber) {
            throw 'PREYSENSE is on a different disk. Refusing to delete it automatically.'
        }
        if ($part.Size -lt 8MB -or $part.Size -gt 32MB) {
            throw 'PREYSENSE partition has an unexpected size. Inspect it manually.'
        }

        $type = [guid]$part.GptType
        if ($type -ne $efiPartitionType -and $type -ne $legacyPartitionType) {
            throw 'PREYSENSE partition has an unexpected GPT type. Inspect it manually.'
        }

        $candidates += $part
    }

    foreach ($part in @(Get-Partition -DiskNumber $windows.DiskNumber)) {
        try {
            if ([guid]$part.GptType -eq $legacyPartitionType -and
                $part.Size -ge 8MB -and $part.Size -le 32MB) {
                $candidates += $part
            }
        } catch {
            # Ignore entries without a usable GPT type.
        }
    }

    $candidates = @($candidates | Sort-Object DiskNumber, PartitionNumber -Unique)
    if ($candidates.Count -gt 1) {
        throw 'Multiple possible PREYSENSE partitions found. Inspect them manually; nothing was deleted.'
    }
    if ($candidates.Count -eq 1) {
        return $candidates[0]
    }
    return $null
}

function Expand-WindowsIntoRemovedPartition {
    param($RemovedPartition)

    # Reclaim only the space removed by this operation, if it is adjacent to C:.
    try {
        $windows = Get-WindowsPartition
        $windowsEnd = $windows.Offset + $windows.Size
        $gap = [long]$RemovedPartition.Offset - [long]$windowsEnd

        if ($RemovedPartition.DiskNumber -ne $windows.DiskNumber -or
            $gap -lt 0 -or $gap -gt 1MB) {
            Write-Info 'Free space is not adjacent to Windows; leaving it unallocated.'
            return
        }

        $supported = Get-PartitionSupportedSize `
            -DiskNumber $windows.DiskNumber `
            -PartitionNumber $windows.PartitionNumber

        $desired = [math]::Min(
            [double]$supported.SizeMax,
            [double]($windows.Size + $RemovedPartition.Size)
        )

        if ($desired -gt $windows.Size) {
            Resize-Partition `
                -DiskNumber $windows.DiskNumber `
                -PartitionNumber $windows.PartitionNumber `
                -Size ([uint64]$desired)
            Write-Info 'Reclaimed the removed partition space for Windows.'
        }
    } catch {
        Write-Warning "Windows partition could not be expanded automatically: $($_.Exception.Message)"
    }
}

# -----------------------------------------------------------------------------
# Uninstall
# -----------------------------------------------------------------------------
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  PreySense | OpenCore Uninstallation' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Info 'This script removes the PreySense WMI class and boot partition.'
Write-Info 'Windows WMI and other EFI partitions are not reset or deleted.'

# Validate dependencies and partition identity before changing WMI or disk.
if (-not (Test-Path $wmiHelper -PathType Leaf)) {
    throw 'Repair-PreySenseWmi.ps1 is missing. Keep it alongside the uninstaller.'
}
$partition = Get-PreySensePartition
$windows = Get-WindowsPartition
if ($partition -and
    $partition.DiskNumber -eq $windows.DiskNumber -and
    $partition.PartitionNumber -eq $windows.PartitionNumber) {
    throw 'Refusing to remove the Windows partition.'
}

Write-Step 1 'Remove only the PreySense WMI registration'
& $wmiHelper -Action Delete
if (-not $?) {
    throw 'WMI cleanup failed. Partition deletion was cancelled.'
}
Write-Info 'WMI cleanup finished. No repository reset was performed.'

Write-Step 2 'Remove the hidden PREYSENSE partition'
if (-not $partition) {
    Write-Info 'No PREYSENSE partition was found; disk cleanup is already complete.'
} else {
    Write-Info "Deleting PREYSENSE: disk $($partition.DiskNumber), partition $($partition.PartitionNumber)."
    # Unmount any assigned drive letter before deleting this verified partition.
    foreach ($accessPath in @($partition.AccessPaths)) {
        if ($accessPath -match '^[A-Za-z]:\\$') {
            Remove-PartitionAccessPath `
                -DiskNumber $partition.DiskNumber `
                -PartitionNumber $partition.PartitionNumber `
                -AccessPath $accessPath `
                -ErrorAction Stop
        }
    }

    Remove-Partition `
        -DiskNumber $partition.DiskNumber `
        -PartitionNumber $partition.PartitionNumber `
        -Confirm:$false

    Expand-WindowsIntoRemovedPartition -RemovedPartition $partition
    Write-Info 'PREYSENSE partition removed.'
}

Write-Host ''
Write-Host 'UNINSTALLATION COMPLETE' -ForegroundColor Green
Write-Host '------------------------------------------------------------' -ForegroundColor DarkGray
Write-Host 'NEXT:' -ForegroundColor Yellow
Write-Host '  1. Restart Windows when convenient.' -ForegroundColor White
Write-Host '  2. If needed, select Windows Boot Manager in UEFI boot options.' -ForegroundColor White
Write-Host '  3. Remove any obsolete PreySense firmware boot/trust entry manually.' -ForegroundColor White
Write-Host 'The already-loaded SSDT will remain until the next reboot.' -ForegroundColor White
