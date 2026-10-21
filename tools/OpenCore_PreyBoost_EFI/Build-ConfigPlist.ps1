param(
    [string]$SourcePlist,
    [string]$DestinationPlist,
    [ValidateRange(85, 999)]
    [int]$PcccCeilingWatts = 85
)
$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
$plistPath = if ($DestinationPlist) { $DestinationPlist } else { Join-Path $scriptDir 'EFI\OC\config.plist' }
$samplePath = if ($SourcePlist) { $SourcePlist } elseif (Test-Path -LiteralPath $plistPath) { $plistPath } else { Join-Path $repoRoot 'tools\Sample.plist' }

[xml]$doc = Get-Content -LiteralPath $samplePath -Raw

function Get-DictValue([System.Xml.XmlElement]$dict, [string]$key) {
    $elements = @($dict.ChildNodes | Where-Object NodeType -eq Element)
    for ($i = 0; $i -lt $elements.Count - 1; $i += 2) {
        if ($elements[$i].Name -eq 'key' -and $elements[$i].InnerText -eq $key) { return $elements[$i + 1] }
    }
    throw "plist key '$key' not found"
}

function Replace-DictValue([System.Xml.XmlElement]$dict, [string]$key, [System.Xml.XmlElement]$replacement) {
    $current = Get-DictValue $dict $key
    [void]$dict.ReplaceChild($doc.ImportNode($replacement, $true), $current)
}

function Read-Fragment([string]$xml) {
    [xml]$fragment = "<root>$xml</root>"
    return $fragment.DocumentElement.FirstChild
}

$rootDict = $doc.plist.ChildNodes | Where-Object NodeType -eq Element | Select-Object -First 1
$acpi = Get-DictValue $rootDict 'ACPI'

$add = Read-Fragment @'
<array>
  <dict>
    <key>Comment</key><string>PreySense isolated runtime power control</string>
    <key>Enabled</key><true/>
    <key>Path</key><string>SSDT-PREYDEV.aml</string>
  </dict>
</array>
'@
$delete = Read-Fragment '<array/>'
$pcccReplacement = [Convert]::FromBase64String('cAwITAEAiIOIUENDWAEAAQA=')
[BitConverter]::GetBytes($PcccCeilingWatts * 1000).CopyTo($pcccReplacement, 2)
$pcccReplacementBase64 = [Convert]::ToBase64String($pcccReplacement)
$patch = Read-Fragment @"
<array>
  <dict>
    <key>Base</key><string></string><key>BaseSkip</key><integer>0</integer>
    <key>Comment</key><string>PreySense: Set Intel IPF PCCC PL1 Limit (85W to ${PcccCeilingWatts}W)</string>
    <key>Count</key><integer>1</integer><key>Enabled</key><true/>
    <key>Find</key><data>cAwITAEAiIOIUENDWAEAAQA=</data><key>Limit</key><integer>0</integer>
    <key>Mask</key><data></data><key>OemTableId</key><data>QUNSUFJEQ1Q=</data>
    <key>Replace</key><data>$pcccReplacementBase64</data><key>ReplaceMask</key><data></data>
    <key>Skip</key><integer>0</integer><key>TableLength</key><integer>20131</integer>
    <key>TableSignature</key><data>U1NEVA==</data>
  </dict>
  <dict>
    <key>Base</key><string></string><key>BaseSkip</key><integer>0</integer>
    <key>Comment</key><string>PreySense: Suppress GPU-to-CPU Cross-Throttling (E3DT 0x0E to 0x0D)</string>
    <key>Count</key><integer>1</integer><key>Enabled</key><true/>
    <key>Find</key><data>RTNEVAoO</data><key>Limit</key><integer>0</integer>
    <key>Mask</key><data></data><key>OemTableId</key><data>QUNSUFJEQ1Q=</data>
    <key>Replace</key><data>RTNEVAoN</data><key>ReplaceMask</key><data></data>
    <key>Skip</key><integer>0</integer><key>TableLength</key><integer>26025</integer>
    <key>TableSignature</key><data>U1NEVA==</data>
  </dict>
</array>
"@

Replace-DictValue $acpi 'Add' $add
Replace-DictValue $acpi 'Delete' $delete
Replace-DictValue $acpi 'Patch' $patch

$misc = Get-DictValue $rootDict 'Misc'
$security = Get-DictValue $misc 'Security'
Replace-DictValue $security 'Vault' (Read-Fragment '<string>Optional</string>')
Replace-DictValue $security 'SecureBootModel' (Read-Fragment '<string>Disabled</string>')
Replace-DictValue $security 'ScanPolicy' (Read-Fragment '<integer>0</integer>')

$platform = Get-DictValue $rootDict 'PlatformInfo'
Replace-DictValue $platform 'Automatic' (Read-Fragment '<false/>')
Replace-DictValue $platform 'UpdateDataHub' (Read-Fragment '<false/>')
Replace-DictValue $platform 'UpdateNVRAM' (Read-Fragment '<false/>')
Replace-DictValue $platform 'UpdateSMBIOS' (Read-Fragment '<false/>')

$kernel = Get-DictValue $rootDict 'Kernel'
Replace-DictValue $kernel 'Add' (Read-Fragment '<array/>')
Replace-DictValue $kernel 'Block' (Read-Fragment '<array/>')
Replace-DictValue $kernel 'Force' (Read-Fragment '<array/>')
Replace-DictValue $kernel 'Patch' (Read-Fragment '<array/>')

$uefi = Get-DictValue $rootDict 'UEFI'
Replace-DictValue $uefi 'Drivers' (Read-Fragment @'
<array>
  <dict>
    <key>Arguments</key><string></string>
    <key>Comment</key><string>OpenCore runtime services</string>
    <key>Enabled</key><true/>
    <key>HideVerbose</key><false/>
    <key>LoadEarly</key><false/>
    <key>Path</key><string>OpenRuntime.efi</string>
  </dict>
</array>
'@)

$nvram = Get-DictValue $rootDict 'NVRAM'
Replace-DictValue $nvram 'Add' (Read-Fragment '<dict/>')
Replace-DictValue $nvram 'Delete' (Read-Fragment '<dict/>')
Replace-DictValue $nvram 'LegacySchema' (Read-Fragment '<dict/>')

$deviceProperties = Get-DictValue $rootDict 'DeviceProperties'
Replace-DictValue $deviceProperties 'Add' (Read-Fragment '<dict/>')
Replace-DictValue $deviceProperties 'Delete' (Read-Fragment '<dict/>')

$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$settings.Indent = $true
$settings.IndentChars = "`t"
$settings.NewLineChars = "`r`n"
$writer = [System.Xml.XmlWriter]::Create($plistPath, $settings)
try { $doc.Save($writer) } finally { $writer.Dispose() }
$serialized = [System.IO.File]::ReadAllText($plistPath).Replace('http://www.apple.com/DTDs/PropertyList-1.0.dtd"[]>', 'http://www.apple.com/DTDs/PropertyList-1.0.dtd">')
[System.IO.File]::WriteAllText($plistPath, $serialized, [System.Text.UTF8Encoding]::new($false))
Write-Host "Generated minimal OpenCore config at $plistPath" -ForegroundColor Green
