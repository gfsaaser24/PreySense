<#
.SYNOPSIS
    Removes PreySense OpenCore, firmware boot options, and the hidden boot partition.
#>
#Requires -RunAsAdministrator

$ErrorActionPreference = 'Stop'
$partitionLabel = 'PREYSENSE'
$efiPartitionType = [guid]'C12A7328-F81F-11D2-BA4B-00A0C93EC93B'
$legacyPartitionType = [guid]'C0DE0001-5052-4559-534E-4F4330310001'
$protectedBcdIds = '^(fwbootmgr|bootmgr|current|default|memdiag|ntldr|recoverysequence)$'

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PreySense OpenCore Uninstaller" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

function Get-FreeDriveLetter {
    $used = @{}
    Get-PSDrive -PSProvider FileSystem | ForEach-Object { $used[$_.Name] = $true }
    foreach ($letter in @('S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z')) {
        if (-not $used.ContainsKey($letter)) { return $letter }
    }
    throw "No free drive letter is available."
}

function Remove-PreySenseBootEntries {
    Write-Host "`n1. Removing PreySense / PreyBoost boot entries..." -ForegroundColor Yellow
    $bcdList = bcdedit /enum all | Out-String
    $blocks = [regex]::Split($bcdList, '(?m)(?=^identifier\s+)')
    $removed = 0
    foreach ($block in $blocks) {
        $idMatch = [regex]::Match($block, '(?im)^identifier\s+(\{[a-fA-F0-9-]+\})')
        if (-not $idMatch.Success) { continue }
        $guid = $idMatch.Groups[1].Value
        $bare = $guid.Trim('{}')
        if ($bare -match $protectedBcdIds) { continue }

        $ours = $block -match 'PreySense|PreyBoost' -or $block -match '\\EFI\\OC\\OpenCore\.efi'
        if (-not $ours) { continue }

        Write-Host "   Deleting boot entry $guid..." -ForegroundColor Yellow
        bcdedit /delete $guid /f | Out-Null
        $removed++
    }
    if ($removed -eq 0) {
        Write-Host "   No PreySense boot entries found." -ForegroundColor Green
    } else {
        Write-Host "   Removed $removed boot entry(ies)." -ForegroundColor Green
    }
}

function Remove-WindowsEspOpenCore {
    param([string]$Letter)
    Write-Host "`n2. Removing leftover OpenCore from the Windows ESP..." -ForegroundColor Yellow
    $mounted = $false
    try {
        mountvol "${Letter}:" /s
        $mounted = $true
        $targetOc = Join-Path "${Letter}:\EFI" "OC"
        if (Test-Path $targetOc) {
            Remove-Item -LiteralPath $targetOc -Recurse -Force
            Write-Host "   Deleted $targetOc." -ForegroundColor Green
        } else {
            Write-Host "   Windows ESP has no OpenCore folder." -ForegroundColor Green
        }
    } catch {
        Write-Host "   Could not clean Windows ESP: $($_.Exception.Message)" -ForegroundColor Yellow
    } finally {
        if ($mounted) {
            mountvol "${Letter}:" /d | Out-Null
        }
    }
}

Remove-PreySenseBootEntries
Remove-WindowsEspOpenCore -Letter (Get-FreeDriveLetter)

Write-Host "`n3. Removing the hidden PreySense partition..." -ForegroundColor Yellow
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
