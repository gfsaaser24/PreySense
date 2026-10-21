$classes = Get-CimClass -Namespace 'root\wmi'
$matched = $classes | Where-Object { $_.CimClassName -match 'Prey|D73644F9|PRYW|WM01' }
Write-Output "Matched classes count: $($matched.Count)"
foreach ($m in $matched) {
    Write-Output "Class: $($m.CimClassName)"
}

# Also let's list all classes that start with Wmi or wmi_
$wmiClasses = $classes | Where-Object { $_.CimClassName -match '^wmi_' }
Write-Output "wmi_ classes count: $($wmiClasses.Count)"
foreach ($wc in $wmiClasses) {
    Write-Output "WMI Class: $($wc.CimClassName)"
}
