# Setup-OpenCoreEFI.ps1
$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$zipPath = Join-Path $scriptDir "OpenCore-1.0.8-RELEASE.zip"
$extractDir = Join-Path $scriptDir "temp_opencore"
$efiDir = Join-Path $scriptDir "EFI"

Write-Host "=== Setting up OpenCore EFI for PreySense ===" -ForegroundColor Cyan

# 1. Download OpenCore release if not already present
if (-not (Test-Path $zipPath) -or (Get-Item $zipPath).Length -lt 1000000) {
    $url = "https://github.com/acidanthera/OpenCorePkg/releases/download/1.0.8/OpenCore-1.0.8-RELEASE.zip"
    Write-Host "Downloading OpenCore 1.0.8 from GitHub..." -ForegroundColor Yellow
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing
    Write-Host "Downloaded successfully ($((Get-Item $zipPath).Length) bytes)." -ForegroundColor Green
} else {
    Write-Host "OpenCore release archive already present." -ForegroundColor Green
}

# 2. Extract minimal files
if (Test-Path $extractDir) {
    Remove-Item -Path $extractDir -Recurse -Force
}
Write-Host "Extracting OpenCore archive..." -ForegroundColor Yellow
Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

# 3. Create target EFI structure
$bootDir = Join-Path $efiDir "BOOT"
$ocDir = Join-Path $efiDir "OC"
$acpiDir = Join-Path $ocDir "ACPI"
$driversDir = Join-Path $ocDir "Drivers"

New-Item -ItemType Directory -Path $bootDir -Force | Out-Null
New-Item -ItemType Directory -Path $ocDir -Force | Out-Null
New-Item -ItemType Directory -Path $acpiDir -Force | Out-Null
New-Item -ItemType Directory -Path $driversDir -Force | Out-Null

# Copy BOOTX64.EFI
$srcBoot = Join-Path $extractDir "X64\EFI\BOOT\BOOTX64.EFI"
Copy-Item -Path $srcBoot -Destination (Join-Path $bootDir "BOOTX64.EFI") -Force

# Copy OpenCore.efi
$srcOc = Join-Path $extractDir "X64\EFI\OC\OpenCore.efi"
Copy-Item -Path $srcOc -Destination (Join-Path $ocDir "OpenCore.efi") -Force

# Copy OpenRuntime.efi
$srcRuntime = Join-Path $extractDir "X64\EFI\OC\Drivers\OpenRuntime.efi"
Copy-Item -Path $srcRuntime -Destination (Join-Path $driversDir "OpenRuntime.efi") -Force

# 4. Clean up temp extraction folder
Remove-Item -Path $extractDir -Recurse -Force

Write-Host "Minimal OpenCore EFI binaries extracted successfully." -ForegroundColor Green
