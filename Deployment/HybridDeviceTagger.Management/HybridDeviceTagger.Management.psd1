# Project-Version: 0.1.20261008.1
# Author: andreas.lucas@microsoft.com (aka Kili)

<#
Copyright 2026 Andreas Lucas

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

SPDX-License-Identifier: Apache-2.0
#>

@{
    RootModule = 'HybridDeviceTagger.Management.psm1'
    ModuleVersion = '0.1.20261008.1'
    GUID = 'c86eeec6-c5ee-44ea-b06f-86d7a0fd4a3f'
    Author = 'Andreas Lucas (aka Kili)'
    CompanyName = 'Andreas Lucas'
    Copyright = 'Copyright 2026 Andreas Lucas. Licensed under Apache-2.0.'
    Description = 'Connects to and operates the HybridDeviceTagger management REST API.'
    PowerShellVersion = '7.2'
    HelpInfoURI = 'https://github.com/Kili69/HybridDeviceTagger/tree/main/Deployment/HybridDeviceTagger.Management'
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
        'Initialize-HybridDeviceTaggerActiveDirectoryCredential'
        'Invoke-HybridDeviceTagging'
        'Invoke-HybridDeviceTaggerPasswordRotation'
    )
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
    PrivateData = @{
        PSData = @{
            Tags = @(
                'ActiveDirectory'
                'AzureFunctions'
                'DeviceManagement'
                'HybridDevices'
                'MicrosoftEntra'
            )
            LicenseUri = 'https://www.apache.org/licenses/LICENSE-2.0'
            ProjectUri = 'https://github.com/Kili69/HybridDeviceTagger'
            IconUri = 'https://raw.githubusercontent.com/Kili69/HybridDeviceTagger/main/assets/kili-logo.png'
            ReleaseNotes = 'https://github.com/Kili69/HybridDeviceTagger/releases'
        }
    }
}