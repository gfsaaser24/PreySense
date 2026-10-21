try {
    $opt = New-Object System.Management.ConnectionOptions
    $opt.EnablePrivileges = $true
    $opt.Impersonation = [System.Management.ImpersonationLevel]::Impersonate
    $scope = New-Object System.Management.ManagementScope('root\WMI', $opt)
    $scope.Connect()

    $query = New-Object System.Management.ObjectQuery('SELECT * FROM PreySenseControl')
    $searcher = New-Object System.Management.ManagementObjectSearcher($scope, $query)
    $items = $searcher.Get()
    Write-Output "PreySenseControl count: $($items.Count)"
    foreach ($item in $items) {
        Write-Output "InstanceName: $($item.Item('InstanceName'))"
        $out = $item.InvokeMethod('Ping', $null, $null)
        Write-Output "Ping Magic: 0x$([Convert]::ToString($out.Item('Magic'), 16))"
    }
} catch {
    Write-Output "Error: $_"
}
