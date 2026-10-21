$origDsdt = ".\docs\acpi_tables\DSDT_ACRSYS_ACRPRDCT_00000002.bin"
$patchedDsdt = ".\acpi_staging\DSDT_PATCHED.bin"
$bytes = [System.IO.File]::ReadAllBytes($origDsdt)

# Offset 0x034CCF has 5F 51 33 30 (_Q30)
$offset = 0x034CCF
$find = [System.Text.Encoding]::ASCII.GetBytes("_Q30")
$replace = [System.Text.Encoding]::ASCII.GetBytes("XQ30")

if ($bytes[$offset] -eq $find[0] -and $bytes[$offset+1] -eq $find[1] -and $bytes[$offset+2] -eq $find[2] -and $bytes[$offset+3] -eq $find[3]) {
    $bytes[$offset] = $replace[0]
    $bytes[$offset+1] = $replace[1]
    $bytes[$offset+2] = $replace[2]
    $bytes[$offset+3] = $replace[3]
    [System.IO.File]::WriteAllBytes($patchedDsdt, $bytes)
    Write-Host "Patched DSDT written to $patchedDsdt successfully."
} else {
    Write-Host "Error: Offset does not match _Q30."
    exit 1
}

# Decompile patched DSDT with iasl to verify syntax & method renaming
$iasl = ".\docs\acpi_tables\iasl.exe"
$outDsl = ".\acpi_staging\DSDT_PATCHED.dsl"
& $iasl -d -p $outDsl $patchedDsdt
