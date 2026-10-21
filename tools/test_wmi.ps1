try {
    $searcher = [System.Management.ManagementObjectSearcher]::new('root\WMI', 'SELECT * FROM AcerGamingFunction')
    $obj = $searcher.Get() | Select-Object -First 1
    if ($null -eq $obj) {
        Write-Host "AcerGamingFunction object not found"
        exit 1
    }
    Write-Host "Found object: $($obj.Path.Path)"
    $inParams = $obj.GetMethodParameters('GetGamingMiscSetting')
    $inParams['gmInput'] = [uint64]11
    $out = $obj.InvokeMethod('GetGamingMiscSetting', $inParams, $null)
    Write-Host "GetGamingMiscSetting output: $($out['gmOutput'])"
} catch {
    Write-Host "Error invoking GetGamingMiscSetting: $_"
    Write-Host $_.Exception.ToString()
}
