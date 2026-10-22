<#
.SYNOPSIS
    Installs the PreySense OpenCore boot files on a small, hidden EFI partition.

.DESCRIPTION
    Replaces an existing PREYSENSE partition (if found) and creates a new 16 MB
    EFI System Partition on the Windows disk. Copies the packaged OpenCore
    files, then marks WMI registration as pending for the next successful boot.

    IMPORTANT:
      - Running this script changes disk partitions immediately.
      - It does not compile preysense.mof on the current boot.
      - After rebooting through the PreySense boot entry, launch PreySense.
        The app validates the loaded SSDT and repairs WMI only if needed.
      - This script never resets the Windows WMI repository.

.EXAMPLE
    .\Install-To-ESP.ps1
    Run in an elevated Windows PowerShell session, from the packaged folder.

.NOTES
    Requires the EFI folder and Repair-PreySenseWmi.ps1 beside this script.
    Back up important data before changing disk partitions.
#>
#Requires -RunAsAdministrator

$ErrorActionPreference = 'Stop'

# -----------------------------------------------------------------------------
# Configuration
# -----------------------------------------------------------------------------
$sourceEfi           = Join-Path $PSScriptRoot 'EFI'
$wmiHelper           = Join-Path $PSScriptRoot 'Repair-PreySenseWmi.ps1'
$partitionLabel      = 'PREYSENSE'
$partitionSizeBytes  = 16MB
$efiPartitionType    = [guid]'C12A7328-F81F-11D2-BA4B-00A0C93EC93B'
$legacyPartitionType = [guid]'C0DE0001-5052-4559-534E-4F4330310001'

function Write-Step {
    param([int]$Number, [string]$Description)
    Write-Host ""
    Write-Host ("[{0}/4] {1}" -f $Number, $Description) -ForegroundColor Cyan
}

function Write-Info {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor White
}

function Get-WindowsPartition {
    $driveLetter = $env:SystemDrive.Substring(0, 1)
    Get-Partition -DriveLetter $driveLetter -ErrorAction Stop
}

function Get-FreeDriveLetter {
    $used = @{}
    Get-PSDrive -PSProvider FileSystem | ForEach-Object { $used[$_.Name] = $true }

    foreach ($letter in @('S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z')) {
        if (-not $used.ContainsKey($letter)) { return $letter }
    }

    throw 'No spare drive letter is available for temporary mounting.'
}

function Get-PreySensePartition {
    # Identify only a distinctly labeled PREYSENSE partition, or the old
    # PreySense-specific GPT type. Do NOT identify a partition by size alone:
    # deleting an arbitrary 8-32 MB EFI partition could destroy another loader.
    $windows = Get-WindowsPartition
    $candidates = @()

    foreach ($volume in @(Get-Volume | Where-Object { $_.FileSystemLabel -eq $partitionLabel })) {
        $part = Get-Partition -Volume $volume -ErrorAction SilentlyContinue |
            Select-Object -First 1

        if (-not $part) {
            throw 'Found a PREYSENSE volume but could not identify its partition. No disk changes were made.'
        }
        if ($part.DiskNumber -ne $windows.DiskNumber) {
            throw 'Found PREYSENSE on a different disk. Refusing to modify it automatically.'
        }
        if ($part.Size -lt 8MB -or $part.Size -gt 32MB) {
            throw 'The PREYSENSE-labeled partition has an unexpected size. Inspect it manually.'
        }

        $type = [guid]$part.GptType
        if ($type -ne $efiPartitionType -and $type -ne $legacyPartitionType) {
            throw 'The PREYSENSE-labeled partition has an unexpected GPT type. Inspect it manually.'
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
            # Ignore entries with no applicable GPT type.
        }
    }

    $candidates = @($candidates | Sort-Object DiskNumber, PartitionNumber -Unique)
    if ($candidates.Count -gt 1) {
        throw 'Multiple possible PREYSENSE partitions were found. Inspect them manually; nothing was deleted.'
    }
    if ($candidates.Count -eq 1) {
        return $candidates[0]
    }
    return $null
}

function Expand-WindowsIntoRemovedPartition {
    param($RemovedPartition)

    # Reclaim at most the space just removed, and only when that space is
    # immediately after the Windows partition. Never consume unrelated free space.
    try {
        $windows = Get-WindowsPartition
        $windowsEnd = $windows.Offset + $windows.Size
        $gap = [long]$RemovedPartition.Offset - [long]$windowsEnd

        if ($RemovedPartition.DiskNumber -ne $windows.DiskNumber -or
            $gap -lt 0 -or $gap -gt 1MB) {
            Write-Info 'Removed partition space is not adjacent to Windows; leaving it unallocated.'
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
            Write-Info 'Returned the removed partition space to Windows.'
        }
    } catch {
        Write-Warning "Windows partition was not expanded: $($_.Exception.Message)"
    }
}

function Remove-ExistingPreySensePartition {
    param($Partition)

    $windows = Get-WindowsPartition
    if ($Partition.DiskNumber -eq $windows.DiskNumber -and
        $Partition.PartitionNumber -eq $windows.PartitionNumber) {
        throw 'Refusing to delete the Windows partition.'
    }

    Write-Info "Removing old PREYSENSE partition: disk $($Partition.DiskNumber), partition $($Partition.PartitionNumber)."
    # Unmount any assigned drive letter before deleting this verified partition.
    foreach ($accessPath in @($Partition.AccessPaths)) {
        if ($accessPath -match '^[A-Za-z]:\\$') {
            Remove-PartitionAccessPath `
                -DiskNumber $Partition.DiskNumber `
                -PartitionNumber $Partition.PartitionNumber `
                -AccessPath $accessPath `
                -ErrorAction Stop
        }
    }

    Remove-Partition `
        -DiskNumber $Partition.DiskNumber `
        -PartitionNumber $Partition.PartitionNumber `
        -Confirm:$false

    Expand-WindowsIntoRemovedPartition -RemovedPartition $Partition
}

function New-PreySensePartition {
    param([int]$DiskNumber, [string]$Letter)

    $windows = Get-WindowsPartition
    if ($windows.DiskNumber -ne $DiskNumber) {
        throw 'Refusing to create PREYSENSE on a disk other than the Windows disk.'
    }

    $supported = Get-PartitionSupportedSize `
        -DiskNumber $windows.DiskNumber `
        -PartitionNumber $windows.PartitionNumber

    $targetSize = $windows.Size - $partitionSizeBytes
    if ($targetSize -lt $supported.SizeMin) {
        throw 'Windows cannot be shrunk by 16 MB. Close applications, reboot, and retry.'
    }

    Write-Info 'Shrinking the Windows partition by 16 MB.'
    Resize-Partition `
        -DiskNumber $windows.DiskNumber `
        -PartitionNumber $windows.PartitionNumber `
        -Size $targetSize

    try {
        $part = New-Partition `
            -DiskNumber $DiskNumber `
            -Size $partitionSizeBytes `
            -GptType "{$($efiPartitionType.ToString().ToUpperInvariant())}"

        $part | Add-PartitionAccessPath -AccessPath "${Letter}:\"
        Format-Volume `
            -DriveLetter $Letter `
            -FileSystem FAT `
            -NewFileSystemLabel $partitionLabel `
            -Confirm:$false | Out-Null

        # Keep the partition hidden during normal Windows operation.
        try {
            Set-Partition `
                -DiskNumber $part.DiskNumber `
                -PartitionNumber $part.PartitionNumber `
                -NoDefaultDriveLetter $true
        } catch {
        }

        return Get-Partition `
            -DiskNumber $part.DiskNumber `
            -PartitionNumber $part.PartitionNumber
    } catch {
        # The shrink succeeded but creation/formatting did not. Do not delete
        # unverified partitions in the error handler; report the failure.
        throw
    }
}

function Copy-OpenCoreFiles {
    param([string]$Letter)

    $targetEfi = Join-Path "${Letter}:\" 'EFI'
    New-Item -ItemType Directory -Path $targetEfi -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceEfi '*') -Destination $targetEfi -Recurse -Force

    foreach ($relativePath in @(
        'BOOT\BOOTX64.EFI',
        'OC\OpenCore.efi',
        'OC\Drivers\OpenRuntime.efi',
        'OC\ACPI\SSDT-PREYDEV.aml',
        'OC\config.plist'
    )) {
        if (-not (Test-Path (Join-Path $targetEfi $relativePath) -PathType Leaf)) {
            throw "OpenCore copy verification failed: $relativePath"
        }
    }
}

# -----------------------------------------------------------------------------
# Install
# -----------------------------------------------------------------------------
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  PreySense | OpenCore Installation' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Info 'This script changes disk partitions as soon as installation begins.'
Write-Info 'The WMI class will be checked by PreySense after reboot.'

Write-Step 1 'Verify the installation package'
foreach ($relativePath in @(
    'BOOT\BOOTX64.EFI',
    'OC\OpenCore.efi',
    'OC\Drivers\OpenRuntime.efi',
    'OC\ACPI\SSDT-PREYDEV.aml',
    'OC\config.plist'
)) {
    if (-not (Test-Path (Join-Path $sourceEfi $relativePath) -PathType Leaf)) {
        throw "Required package file is missing: EFI\$relativePath"
    }
}
if (-not (Test-Path $wmiHelper -PathType Leaf)) {
    throw 'Repair-PreySenseWmi.ps1 is missing. Keep it alongside the installer.'
}
Write-Info 'Required package files found.'

Write-Step 2 'Create the hidden PREYSENSE partition'
$windows = Get-WindowsPartition
$existing = Get-PreySensePartition
$mountLetter = Get-FreeDriveLetter

if ($existing) {
    Remove-ExistingPreySensePartition -Partition $existing
}

$partition = New-PreySensePartition -DiskNumber $windows.DiskNumber -Letter $mountLetter
Write-Info "Created a 16 MB EFI partition on disk $($partition.DiskNumber)."

Write-Step 3 'Copy OpenCore and remove the temporary drive letter'
try {
    Copy-OpenCoreFiles -Letter $mountLetter
    Write-Info 'OpenCore files copied and verified.'
} finally {
    # The installer temporarily assigns this letter; the ESP stays hidden.
    try {
        Remove-PartitionAccessPath `
            -DiskNumber $partition.DiskNumber `
            -PartitionNumber $partition.PartitionNumber `
            -AccessPath "${mountLetter}:\" `
            -ErrorAction Stop
        Write-Info 'Temporary drive letter removed.'
    } catch {
        Write-Warning "Could not remove the temporary drive letter: $($_.Exception.Message)"
    }
}

Write-Step 4 'Schedule WMI verification for the next boot'
& $wmiHelper -Action Arm
if (-not $?) {
    throw 'Could not arm WMI registration. Installation is incomplete.'
}
Write-Info 'WMI registration is pending; no WMI class was changed on this boot.'

Write-Host ''
Write-Host 'INSTALLATION COMPLETE' -ForegroundColor Green
Write-Host '------------------------------------------------------------' -ForegroundColor DarkGray
Write-Host 'NEXT: Configure your firmware, then restart Windows.' -ForegroundColor Yellow
Write-Host ''
Write-Host 'For Acer BIOS with the "trusted UEFI file" feature:' -ForegroundColor White
Write-Host '  1. Enter BIOS setup (usually F2 during startup).' -ForegroundColor White
Write-Host '  2. In Security, unlock trusted-file enrollment if required.' -ForegroundColor White
Write-Host '     Some Acer firmware requires a supervisor password.' -ForegroundColor White
Write-Host '  3. Select "Select an UEFI file as trusted for executing".' -ForegroundColor White
Write-Host '  4. On PREYSENSE, enroll these files if your BIOS requires them:' -ForegroundColor White
Write-Host '       EFI\BOOT\BOOTX64.EFI' -ForegroundColor White
Write-Host '       EFI\OC\OpenCore.efi' -ForegroundColor White
Write-Host '       EFI\OC\Drivers\OpenRuntime.efi' -ForegroundColor White
Write-Host '  5. Choose the PreySense/OpenCore entry in boot order, if present.' -ForegroundColor White
Write-Host '  6. Save BIOS settings and restart through that entry.' -ForegroundColor White
Write-Host ''
Write-Host 'AFTER WINDOWS STARTS:' -ForegroundColor Cyan
Write-Host '  Open PreySense. It checks the loaded SSDT and repairs WMI only' -ForegroundColor White
Write-Host '  if the expected interface is loaded and the class is stale.' -ForegroundColor White
Write-Host '  If the app says "Restart Required", boot through PreySense.' -ForegroundColor White
Write-Host ''

$restart = Read-Host 'Restart Windows now? (Y/N)'
if ($restart -match '^[Yy]') {
    Write-Host 'Restarting Windows...' -ForegroundColor Yellow
    shutdown.exe /r /t 0 /c 'PreySense OpenCore installation completed.'
} else {
    Write-Info 'Restart skipped. The current boot still uses its previously loaded SSDT.'
}
