<#
.SYNOPSIS
    Prepares a USB flash drive with the PreyBoost OpenCore EFI.
.DESCRIPTION
    Copies the EFI folder to a FAT32-formatted USB drive.
    Allows zero-risk testing of ACPI unlocks via the F12 Boot Menu.
.PARAMETER DriveLetter
    Drive letter of the USB flash drive (e.g. 'E:' or 'E').
#>
param(
    [Parameter(Mandatory=$false)]
    [string]$DriveLetter
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$sourceEfi = Join-Path $scriptDir "EFI"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PreyBoost OpenCore USB Preparation Utility" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path $sourceEfi)) {
    Write-Error "Source EFI directory not found at $sourceEfi"
}

if (-not $DriveLetter) {
    Write-Host "`nDetecting removable USB drives..." -ForegroundColor Yellow
    $usbDrives = Get-Volume | Where-Object { $_.DriveType -eq 'Removable' -and $_.DriveLetter }
    if (-not $usbDrives) {
        # Fallback to all drives except C:
        $usbDrives = Get-Volume | Where-Object { $_.DriveLetter -and $_.DriveLetter -ne 'C' }
    }
    
    Write-Host "Available drives:" -ForegroundColor White
    $usbDrives | ForEach-Object {
        Write-Host "  $($_.DriveLetter): [$($_.FileSystem)] $($_.FileSystemLabel) - $([math]::Round($_.SizeRemaining/1GB, 1)) GB free"
    }
    
    $DriveLetter = Read-Host "`nEnter the target drive letter (e.g. E)"
}

$cleanDrive = $DriveLetter.TrimEnd(':') + ":"
$targetPath = Join-Path "$cleanDrive\" "EFI"

if (-not (Test-Path "$cleanDrive\")) {
    Write-Error "Drive $cleanDrive does not exist."
}

$vol = Get-Volume -DriveLetter $DriveLetter.TrimEnd(':')
if ($vol.FileSystem -ne 'FAT32') {
    Write-Warning "Drive $cleanDrive is formatted as $($vol.FileSystem). UEFI requires FAT32."
    Write-Host "Please format your USB drive as FAT32 if your BIOS does not recognize it." -ForegroundColor Yellow
}

Write-Host "`nCopying PreyBoost EFI to $targetPath..." -ForegroundColor Yellow
if (-not (Test-Path $targetPath)) {
    New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
}

Copy-Item -Path "$sourceEfi\*" -Destination $targetPath -Recurse -Force

$usbConfig = Join-Path $targetPath "OC\config.plist"
if (Test-Path $usbConfig) {
    $plist = Get-Content -LiteralPath $usbConfig -Raw
    $fixed = $plist
    $fixed = [regex]::Replace($fixed, '<key>Vault</key>\s*<string>.*?</string>', "<key>Vault</key>`r`n`t`t`t`t<string>Optional</string>", 1)
    $fixed = [regex]::Replace($fixed, '<key>SecureBootModel</key>\s*<string>.*?</string>', "<key>SecureBootModel</key>`r`n`t`t`t`t<string>Disabled</string>", 1)
    $fixed = [regex]::Replace($fixed, '<key>ScanPolicy</key>\s*<integer>.*?</integer>', "<key>ScanPolicy</key>`r`n`t`t`t`t<integer>0</integer>", 1)
    $fixed = [regex]::Replace($fixed, '<key>Automatic</key>\s*<true\s*/>', "<key>Automatic</key>`r`n`t`t`t<false />", 1)
    $fixed = [regex]::Replace($fixed, '<key>UpdateDataHub</key>\s*<true\s*/>', "<key>UpdateDataHub</key>`r`n`t`t`t<false />", 1)
    $fixed = [regex]::Replace($fixed, '<key>UpdateNVRAM</key>\s*<true\s*/>', "<key>UpdateNVRAM</key>`r`n`t`t`t<false />", 1)
    $fixed = [regex]::Replace($fixed, '<key>UpdateSMBIOS</key>\s*<true\s*/>', "<key>UpdateSMBIOS</key>`r`n`t`t`t<false />", 1)
    if ($fixed -ne $plist) {
        [System.IO.File]::WriteAllText($usbConfig, $fixed, [System.Text.UTF8Encoding]::new($false))
        Write-Host "Applied Windows-safe OpenCore security settings (no vault, no Apple Secure Boot)." -ForegroundColor Yellow
    }
}

Write-Host "`n[SUCCESS] PreyBoost OpenCore EFI successfully written to $cleanDrive\EFI!" -ForegroundColor Green
Write-Host "`nHow to test:" -ForegroundColor White
Write-Host "  1. Restart your laptop and press F12 during boot to open the Acer Boot Menu."
Write-Host "  2. Select your USB drive."
Write-Host "  3. OpenCore will inject SSDT-PREYDEV and boot Windows."
Write-Host "  4. Check power limits in PreySense or nvidia-smi."
Write-Host "  5. If anything goes wrong, simply unplug the USB drive to boot stock Windows."
