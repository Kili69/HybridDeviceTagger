#Requires -Version 7.2

<#
.SYNOPSIS
Tests forest-domain management commands without acquiring tokens or calling Azure.

.EXAMPLE
pwsh -NoProfile -File tests\ManagementModule.Tests.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$manifest = Join-Path (Split-Path $PSScriptRoot -Parent) 'installationspackage\HybridDeviceTagger.Management\HybridDeviceTagger.Management.psd1'
$module = Import-Module $manifest -PassThru -Force
try {
    & $module {
        function Assert-HybridDeviceTaggerConnection {}
        $script:Connection = [pscustomobject]@{ ApiBaseUri = 'https://test.azurewebsites.net/api/management' }
        $script:Requests = [Collections.Generic.List[object]]::new()
        function Invoke-HybridDeviceTaggerRequest {
            param($Method, $Path, $Body, $ETag, [switch]$IncludeResponseHeaders)
            $script:Requests.Add([pscustomobject]@{
                Method = $Method; Path = $Path; Body = $Body; ETag = $ETag
            })
            if ($IncludeResponseHeaders) {
                return [pscustomobject]@{ Content = @{ domain = 'wrong.example' }; ETag = '"version"' }
            }
            [pscustomobject]@{ domain = 'contoso.com'; source = 'blob' }
        }

        Set-HybridDeviceTaggerActiveDirectoryConfiguration -Domain contoso.com -WhatIf
        if ($script:Requests.Count -ne 0) { throw 'WhatIf must not send requests.' }

        $result = Set-HybridDeviceTaggerActiveDirectoryConfiguration -Domain contoso.com -Confirm:$false
        if ($script:Requests.Count -ne 2 -or
            $script:Requests[0].Method -ne 'Get' -or
            $script:Requests[1].Method -ne 'Put' -or
            $script:Requests[1].Path -ne 'active-directory' -or
            $script:Requests[1].ETag -ne '"version"' -or
            $script:Requests[1].Body.domain -ne 'contoso.com' -or
            $result.domain -ne 'contoso.com') {
            throw 'Set must send the requested domain to the version-protected management route.'
        }
        $null = Get-HybridDeviceTaggerActiveDirectoryConfiguration
        if ($script:Requests.Count -ne 3 -or $script:Requests[2].Method -ne 'Get') {
            throw 'Get must read the effective forest-domain configuration.'
        }
    }
}
finally {
    Remove-Module $module
}
Write-Host 'Management module tests passed.'
