# Project-Version: 0.1.20260905.16
# Author: andreas.lucas@microsoft.com (aka Kili)

@{
    RootModule = 'HybridDeviceTagger.Management.psm1'
    ModuleVersion = '0.1.20260905.16'
    GUID = 'c86eeec6-c5ee-44ea-b06f-86d7a0fd4a3f'
    Author = 'Andreas Lucas (aka Kili)'
    CompanyName = 'Andreas Lucas'
    Copyright = '(c) 2026 Andreas Lucas. All rights reserved.'
    Description = 'Connects to and operates the HybridDeviceTagger management REST API.'
    PowerShellVersion = '7.2'
    FormatsToProcess = @('HybridDeviceTagger.Management.Format.ps1xml')
    FunctionsToExport = @(
        'Connect-HybridDeviceTagger'
        'Get-HybridDeviceTaggerRule'
        'Get-HybridDeviceTaggerLastRunStatus'
        'Get-HybridDeviceTaggerPasswordRotationStatus'
        'Get-HybridDeviceTaggerTrace'
        'Get-HybridDeviceTaggerTrustedCertificate'
        'Add-HybridDeviceTaggerRule'
        'Add-HybridDeviceTaggerTrustedCertificate'
        'Remove-HybridDeviceTaggerRule'
        'Remove-HybridDeviceTaggerTrustedCertificate'
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
            ProjectUri = 'https://github.com/anluca_microsoft/HybridDeviceTagger'
        }
    }
}