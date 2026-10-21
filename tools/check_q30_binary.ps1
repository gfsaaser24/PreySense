$dsdtPath = ".\docs\acpi_tables\DSDT_ACRSYS_ACRPRDCT_00000002.bin"
$bytes = [System.IO.File]::ReadAllBytes($dsdtPath)
$target = [System.Text.Encoding]::ASCII.GetBytes("_Q30")
$indices = [System.Collections.Generic.List[int]]::new()

for ($idx = 0; $idx -le ($bytes.Length - 4); $idx++) {
    if ($bytes[$idx] -eq $target[0] -and $bytes[$idx+1] -eq $target[1] -and $bytes[$idx+2] -eq $target[2] -and $bytes[$idx+3] -eq $target[3]) {
        $indices.Add($idx)
    }
}

Write-Host "Total matches for '_Q30' in DSDT: $($indices.Count)"
foreach ($i in $indices) {
    $start = [Math]::Max(0, $i - 4)
    $len = [Math]::Min(16, $bytes.Length - $start)
    $hex = [BitConverter]::ToString($bytes, $start, $len)
    Write-Host ("Offset 0x{0:X6} ({0}): {1}" -f $i, $hex)
}
