<#
.SYNOPSIS
    Installs PreySense OpenCore on a hidden FAT partition and boots it from UEFI firmware.
.DESCRIPTION
    Creates a 16 MB EFI System Partition on the Windows disk and copies OpenCore there.
    An existing PREYSENSE partition is deleted first so the new SSDT is the only copy.
    The volume has no Windows drive letter. Enroll the EFI files in BIOS Secure Boot
    and set PreySense first in BIOS boot order.
#>
#Requires -RunAsAdministrator

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$sourceEfi = Join-Path $scriptDir "EFI"
$partitionSizeBytes = 16MB
$partitionLabel = 'PREYSENSE'
$efiPartitionType = [guid]'C12A7328-F81F-11D2-BA4B-00A0C93EC93B'
$legacyPartitionType = [guid]'C0DE0001-5052-4559-534E-4F4330310001'

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PreySense OpenCore Installer" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path (Join-Path $sourceEfi "OC\OpenCore.efi"))) {
    Write-Error "Source OpenCore files not found at $sourceEfi"
}

function Get-FreeDriveLetter {
    $used = @{}
    Get-PSDrive -PSProvider FileSystem | ForEach-Object { $used[$_.Name] = $true }
    foreach ($letter in @('S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z')) {
        if (-not $used.ContainsKey($letter)) { return $letter }
    }
    throw "No free drive letter is available to mount the PreySense partition."
}

function Get-PartitionForVolume {
    param($Volume)
    if ($Volume.DriveLetter) {
        return Get-Partition -DriveLetter $Volume.DriveLetter
    }
    Get-Partition | Where-Object {
        $_.AccessPaths -contains $Volume.Path
    } | Select-Object -First 1
}

function Get-PreySensePartition {
    $volume = Get-Volume | Where-Object { $_.FileSystemLabel -eq $partitionLabel } | Select-Object -First 1
    if ($volume) {
        $fromLabel = Get-PartitionForVolume -Volume $volume
        if ($fromLabel) { return $fromLabel }
    }

    Get-Partition | Where-Object {
        try {
            $type = [guid]$_.GptType
            $type -eq $legacyPartitionType -or
            ($type -eq $efiPartitionType -and $_.Size -le 32MB -and $_.Size -ge 8MB)
        } catch { $false }
    } | Select-Object -First 1
}

function Get-WindowsSystemPartition {
    $sys = $env:SystemDrive.TrimEnd('\')
    $letter = $sys.Substring(0, 1)
    Get-Partition -DriveLetter $letter
}

function Remove-PreySensePartition {
    param($Partition)

    $windows = Get-WindowsSystemPartition
    if ($Partition.DiskNumber -eq $windows.DiskNumber -and $Partition.PartitionNumber -eq $windows.PartitionNumber) {
        throw "Refusing to delete the Windows system partition."
    }

    if ($Partition.DriveLetter) {
        Remove-PartitionAccessPath -DriveLetter $Partition.DriveLetter -AccessPath "$($Partition.DriveLetter):\" -ErrorAction SilentlyContinue
    }
    foreach ($path in @($Partition.AccessPaths)) {
        if ($path -and $path -like '?:\') {
            Remove-PartitionAccessPath -DiskNumber $Partition.DiskNumber -PartitionNumber $Partition.PartitionNumber -AccessPath $path -ErrorAction SilentlyContinue
        }
    }

    Write-Host "   Deleting disk $($Partition.DiskNumber) partition $($Partition.PartitionNumber)..." -ForegroundColor Yellow
    Remove-Partition -DiskNumber $Partition.DiskNumber -PartitionNumber $Partition.PartitionNumber -Confirm:$false
    Write-Host "   Old partition removed." -ForegroundColor Green

    try {
        $windows = Get-WindowsSystemPartition
        $supported = Get-PartitionSupportedSize -DiskNumber $windows.DiskNumber -PartitionNumber $windows.PartitionNumber
        if ($supported.SizeMax -gt $windows.Size) {
            Resize-Partition -DiskNumber $windows.DiskNumber -PartitionNumber $windows.PartitionNumber -Size $supported.SizeMax
            Write-Host "   Returned the old partition space to Windows." -ForegroundColor Green
        }
    } catch {
        Write-Host "   Old partition deleted. Windows could not be expanded automatically: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function New-PreySensePartition {
    param([int]$DiskNumber, [string]$Letter)

    $windows = Get-WindowsSystemPartition
    if ($windows.DiskNumber -ne $DiskNumber) {
        throw "Refusing to create a partition on disk $DiskNumber; Windows is on disk $($windows.DiskNumber)."
    }

    $supported = Get-PartitionSupportedSize -DiskNumber $windows.DiskNumber -PartitionNumber $windows.PartitionNumber
    $targetSize = $windows.Size - $partitionSizeBytes
    if ($targetSize -lt $supported.SizeMin) {
        throw "Not enough shrinkable space on $($env:SystemDrive). Close apps, reboot, and try again."
    }

    Write-Host "   Shrinking Windows partition by 16 MB..." -ForegroundColor Yellow
    Resize-Partition -DiskNumber $windows.DiskNumber -PartitionNumber $windows.PartitionNumber -Size $targetSize

    try {
        $part = New-Partition -DiskNumber $DiskNumber -Size $partitionSizeBytes -GptType "{$($efiPartitionType.ToString().ToUpperInvariant())}"
        $part | Add-PartitionAccessPath -AccessPath "${Letter}:\"
        Format-Volume -DriveLetter $Letter -FileSystem FAT -NewFileSystemLabel $partitionLabel -Confirm:$false | Out-Null
        try {
            Set-Partition -DiskNumber $part.DiskNumber -PartitionNumber $part.PartitionNumber -NoDefaultDriveLetter $true
        } catch { }
        return Get-Partition -DiskNumber $part.DiskNumber -PartitionNumber $part.PartitionNumber
    } catch {
        try {
            $windows = Get-WindowsSystemPartition
            $supported = Get-PartitionSupportedSize -DiskNumber $windows.DiskNumber -PartitionNumber $windows.PartitionNumber
            if ($supported.SizeMax -gt $windows.Size) {
                Resize-Partition -DiskNumber $windows.DiskNumber -PartitionNumber $windows.PartitionNumber -Size $supported.SizeMax
            }
        } catch { }
        throw
    }
}

function Mount-PreySensePartition {
    param($Partition, [string]$Letter)
    if ($Partition.DriveLetter) {
        return [string]$Partition.DriveLetter
    }
    Add-PartitionAccessPath -DiskNumber $Partition.DiskNumber -PartitionNumber $Partition.PartitionNumber -AccessPath "${Letter}:\"
    return $Letter
}

function Dismount-PreySensePartition {
    param($Partition, [string]$Letter)
    try {
        Remove-PartitionAccessPath -DiskNumber $Partition.DiskNumber -PartitionNumber $Partition.PartitionNumber -AccessPath "${Letter}:\" -ErrorAction Stop
    } catch {
        if ($Partition.DriveLetter) {
            Remove-PartitionAccessPath -DriveLetter $Partition.DriveLetter -AccessPath "$($Partition.DriveLetter):\" -ErrorAction SilentlyContinue
        }
    }
}

function Update-PreySenseEfiFiles {
    param([string]$Letter)

    $targetEfi = Join-Path "${Letter}:\" "EFI"
    New-Item -ItemType Directory -Path (Join-Path $targetEfi "BOOT") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $targetEfi "OC\Drivers") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $targetEfi "OC\ACPI") -Force | Out-Null
    Copy-Item -Path "$sourceEfi\*" -Destination $targetEfi -Recurse -Force
    if (-not (Test-Path (Join-Path $targetEfi "BOOT\BOOTX64.EFI"))) {
        throw "OpenCore BOOTX64.EFI is missing after copy."
    }
    if (-not (Test-Path (Join-Path $targetEfi "OC\OpenCore.efi"))) {
        throw "OpenCore.efi is missing after copy."
    }
    if (-not (Test-Path (Join-Path $targetEfi "OC\Drivers\OpenRuntime.efi"))) {
        throw "OpenRuntime.efi is missing after copy."
    }
}

$mountLetter = Get-FreeDriveLetter
$windows = Get-WindowsSystemPartition
$existing = Get-PreySensePartition
if ($existing) {
    Write-Host "`n1. Removing the existing hidden PREYSENSE partition..." -ForegroundColor Yellow
    Remove-PreySensePartition -Partition $existing
}

Write-Host "`nCreating a new 16 MB hidden PreySense partition..." -ForegroundColor Yellow
$partition = New-PreySensePartition -DiskNumber $windows.DiskNumber -Letter $mountLetter
$mountLetter = if ($partition.DriveLetter) { [string]$partition.DriveLetter } else { Mount-PreySensePartition -Partition $partition -Letter $mountLetter }
Write-Host "   Created EFI System partition labeled $partitionLabel (no Windows drive letter)." -ForegroundColor Green

try {
    Write-Host "`n2. Writing OpenCore files to ${mountLetter}:\EFI ..." -ForegroundColor Yellow
    Update-PreySenseEfiFiles -Letter $mountLetter
    Write-Host "   Files updated." -ForegroundColor Green
}
finally {
    Dismount-PreySensePartition -Partition (Get-Partition -DiskNumber $partition.DiskNumber -PartitionNumber $partition.PartitionNumber) -Letter $mountLetter
}

Write-Host "`n[SUCCESS] Installation complete." -ForegroundColor Green
Write-Host "OpenCore is on the hidden PREYSENSE EFI partition. It has no Windows drive letter;" -ForegroundColor White
Write-Host "BIOS can still browse it. Enroll all three files (repeat for each):" -ForegroundColor White
Write-Host "  1. Open BIOS -> Security -> set a supervisor password if asked, then reboot." -ForegroundColor White
Write-Host "  2. Open BIOS -> Security -> Select an UEFI file as trusted for executing" -ForegroundColor White
Write-Host "     PREYSENSE -> EFI -> BOOT -> BOOTX64.EFI" -ForegroundColor White
Write-Host "  3. Repeat: PREYSENSE -> EFI -> OC -> OpenCore.efi" -ForegroundColor White
Write-Host "  4. Repeat: PREYSENSE -> EFI -> OC -> Drivers -> OpenRuntime.efi" -ForegroundColor White
Write-Host "  5. Boot -> put PreySense first in the boot order. Save and exit." -ForegroundColor White
Write-Host ""
$restart = Read-Host "Restart Windows now? (Y/N)"
if ($restart -match '^[Yy]') {
    Write-Host "Restarting..." -ForegroundColor Yellow
    shutdown.exe /r /t 0 /c "PreySense OpenCore install finished. Restarting."
} else {
    Write-Host "Restart skipped. Reboot from Windows when you are ready." -ForegroundColor White
}
