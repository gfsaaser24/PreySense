$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$stagingDir = Join-Path $repoRoot 'acpi_staging'
$mofPath = Join-Path $stagingDir 'preysense.mof'
$bmfPath = Join-Path $stagingDir 'preysense.bmf'
$dslPath = Join-Path $stagingDir 'SSDT-PREYDEV.dsl'
$amlPath = Join-Path $stagingDir 'SSDT-PREYDEV.aml'
$iaslPath = Join-Path $repoRoot 'docs\acpi_tables\iasl.exe'

& mofcomp "-B:$bmfPath" $mofPath
if ($LASTEXITCODE -ne 0) { throw "mofcomp failed with exit code $LASTEXITCODE" }

$bytes = [System.IO.File]::ReadAllBytes($bmfPath)
$lines = for ($offset = 0; $offset -lt $bytes.Length; $offset += 16) {
    $end = [Math]::Min($offset + 15, $bytes.Length - 1)
    '                ' + (($bytes[$offset..$end] | ForEach-Object { '0x{0:X2}' -f $_ }) -join ', ') + $(if ($end -lt $bytes.Length - 1) { ',' } else { '' })
}
$buffer = "Name (WQ01, Buffer (0x{0:X4})`r`n            {{`r`n{1}`r`n            }})" -f $bytes.Length, ($lines -join "`r`n")
$dsl = [System.IO.File]::ReadAllText($dslPath)
$updated = [regex]::Replace($dsl, 'Name \(WQ01, Buffer \(0x[0-9A-Fa-f]+\)\s*\{[\s\S]*?\n\s*\}\)', $buffer, 1)
if ($updated -eq $dsl) { throw 'Could not locate WQ01 buffer in SSDT-PREYDEV.dsl' }
[System.IO.File]::WriteAllText($dslPath, $updated, [System.Text.UTF8Encoding]::new($false))

& $iaslPath -ve -p $amlPath $dslPath
if ($LASTEXITCODE -ne 0) { throw "iASL failed with exit code $LASTEXITCODE" }
Write-Host "Generated $amlPath from $mofPath" -ForegroundColor Green
