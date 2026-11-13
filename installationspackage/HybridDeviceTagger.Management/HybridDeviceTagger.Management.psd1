# Project-Version: source
# Author: andreas.lucas@outlook.com (aka Kili)

@{
    RootModule = 'HybridDeviceTagger.Management.psm1'
    ModuleVersion = '0.0'
    GUID = 'c86eeec6-c5ee-44ea-b06f-86d7a0fd4a3f'
    Author = 'Andreas Lucas (aka Kili)'
    CompanyName = 'Andreas Lucas'
    Copyright = 'Copyright 2026 Andreas Lucas. Licensed under Apache-2.0.'
    Description = 'Connects to and operates the HybridDeviceTagger management REST API.'
    PowerShellVersion = '7.2'
    FormatsToProcess = @('HybridDeviceTagger.Management.Format.ps1xml')
    FunctionsToExport = @(
        'Connect-HybridDeviceTagger'
        'Get-HybridDeviceTaggerRule'
        'Get-HybridDeviceTaggerLastRunStatus'
        'Get-HybridDeviceTaggerPasswordRotationStatus'
        'Get-HybridDeviceTaggerTrace'
        'Add-HybridDeviceTaggerRule'
        'Remove-HybridDeviceTaggerRule'
        'Set-HybridDeviceTaggerRuleConfiguration'
        'Invoke-HybridDeviceTagging'
        'Invoke-HybridDeviceTaggerPasswordRotation'
    )
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
    PrivateData = @{
        PSData = @{
            Tags = @('AzureFunctions', 'MicrosoftEntra', 'HybridDevices')
            LicenseUri = 'https://github.com/Kili69/HybridDeviceTagger/blob/main/LICENSE'
            ProjectUri = 'https://github.com/anluca_microsoft/HybridDeviceTagger'
        }
    }
}