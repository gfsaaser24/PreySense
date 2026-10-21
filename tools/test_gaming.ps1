try {
    $searcher = New-Object System.Management.ManagementObjectSearcher('root\WMI', 'SELECT * FROM AcerGamingFunction')
    $items = $searcher.Get()
    Write-Output "Items Count: $($items.Count)"
    foreach ($item in $items) {
        Write-Output "Found: $($item.Item('InstanceName'))"
    }
} catch {
    Write-Output "Error: $_"
}
