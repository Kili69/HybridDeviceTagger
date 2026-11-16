#Requires -Version 7.2

<#
.SYNOPSIS
Tests forest-domain, service-account, and trust-certificate management commands without acquiring tokens or calling Azure.

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

        $script:Requests.Clear()
        $credential = [pscredential]::new('svc@contoso.com', (ConvertTo-SecureString 'P@ss' -AsPlainText -Force))
        Set-HybridDeviceTaggerServiceAccount -Credential $credential -WhatIf
        $null = Set-HybridDeviceTaggerServiceAccount -Credential $credential -Confirm:$false
        if ($script:Requests.Count -ne 1 -or $script:Requests[0].Method -ne 'Put' -or
            $script:Requests[0].Path -ne 'portal/active-directory/account' -or
            $script:Requests[0].Body.username -ne 'svc@contoso.com' -or
            $script:Requests[0].Body.password -ne 'P@ss') {
            throw 'Set-HybridDeviceTaggerServiceAccount must send the account to the portal route only after confirmation.'
        }

        $script:Requests.Clear()
        $rsa = [System.Security.Cryptography.RSA]::Create(2048)
        $certificateRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=Test Root', $rsa, 'SHA256', [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $certificate = $certificateRequest.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(30))
        $certificatePath = Join-Path ([IO.Path]::GetTempPath()) "hdt-test-$([guid]::NewGuid()).cer"
        try {
            [IO.File]::WriteAllBytes($certificatePath, $certificate.Export('Cert'))
            $null = Add-HybridDeviceTaggerTrustedCertificate -Path $certificatePath -Confirm:$false
            if ($script:Requests.Count -ne 1 -or $script:Requests[0].Path -ne 'portal/certificates' -or
                $script:Requests[0].Body.certificate -notlike '-----BEGIN CERTIFICATE-----*') {
                throw 'Add-HybridDeviceTaggerTrustedCertificate must send only the public PEM certificate.'
            }
            $null = Remove-HybridDeviceTaggerTrustedCertificate -Thumbprint $certificate.Thumbprint.ToLowerInvariant() -Confirm:$false
            if ($script:Requests[1].Method -ne 'Delete' -or
                $script:Requests[1].Path -ne "portal/certificates/$($certificate.Thumbprint)") {
                throw 'Remove-HybridDeviceTaggerTrustedCertificate must delete the uppercase thumbprint route.'
            }
        }
        finally {
            Remove-Item -LiteralPath $certificatePath -ErrorAction SilentlyContinue
            $certificate.Dispose()
            $rsa.Dispose()
        }
    }
}
finally {
    Remove-Module $module
}
Write-Host 'Management module tests passed.'
