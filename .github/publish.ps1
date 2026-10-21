param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "dist"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptDir
Set-Location $root

$outputPath = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }

if (Test-Path -LiteralPath $outputPath) {
    $locked = @()
    Get-ChildItem -LiteralPath $outputPath -Force | ForEach-Object {
        try {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force
        }
        catch {
            $locked += $_.Name
        }
    }
    if ($locked -contains "PreySense.exe") {
        throw "Could not replace PreySense.exe in '$outputPath'. Close the running app and publish again."
    }
    if ($locked.Count -gt 0) {
        Write-Host "Left locked files in place: $($locked -join ', ')"
    }
}

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

dotnet publish PreySense.Frontend\PreySense.Frontend.csproj `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $outputPath

$publishedExe = Join-Path $outputPath "PreySense.Frontend.exe"
$releaseExe = Join-Path $outputPath "PreySense.exe"
if (Test-Path -LiteralPath $publishedExe) {
    Move-Item -LiteralPath $publishedExe -Destination $releaseExe -Force
}

$pdb = Join-Path $outputPath "PreySense.pdb"
if (Test-Path -LiteralPath $pdb) {
    Remove-Item -LiteralPath $pdb -Force
}
$frontendPdb = Join-Path $outputPath "PreySense.Frontend.pdb"
if (Test-Path -LiteralPath $frontendPdb) {
    Remove-Item -LiteralPath $frontendPdb -Force
}

$sys = Join-Path $outputPath "PreySense.sys"
if (Test-Path -LiteralPath $sys) {
    try {
        Remove-Item -LiteralPath $sys -Force
    }
    catch {
        Write-Host "PreySense.sys is locked. Close PreySense so it is not left next to the exe."
    }
}

Write-Host "Published:"
Get-ChildItem -LiteralPath $outputPath -Force | Select-Object Name, Length, LastWriteTime
