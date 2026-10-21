# Shared initialization loader for test tools
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

if (-not ([System.Management.Automation.PSTypeName]'PreySense.TestTools.WindowHelper').Type) {
    $csSource = Get-Content -Path "$PSScriptRoot\PreySenseNative.cs" -Raw
    Add-Type -TypeDefinition $csSource
}
