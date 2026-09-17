#Requires -Version 7.2
# Project-Version: 0.1.20260917.1
# Author: andreas.lucas@microsoft.com (aka Kili)

<#
DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
#>

Set-StrictMode -Version Latest
$script:Connection = $null

function Initialize-HybridDeviceTaggerToken {
    if (-not (Get-Command az -CommandType Application -ErrorAction SilentlyContinue)) {
        throw 'Azure CLI is required. Install Azure CLI, run az login, and try again.'
    }

    $tokenResponse = az account get-access-token `
        --scope "api://$($script:Connection.ClientId)/.default" `
        --output json `
        --only-show-errors | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$tokenResponse.accessToken)) {
        throw "Unable to obtain an Entra access token for api://$($script:Connection.ClientId). Run 'az login --scope api://$($script:Connection.ClientId)/.default' and try again."
    }

    $script:Connection.AccessToken = [string]$tokenResponse.accessToken
    $script:Connection.ExpiresOn = if ($tokenResponse.PSObject.Properties['expires_on']) {
        [DateTimeOffset]::FromUnixTimeSeconds([long]$tokenResponse.expires_on)
    }
    elseif ($tokenResponse.PSObject.Properties['expiresOn']) {
        [DateTimeOffset]::Parse(
            [string]$tokenResponse.expiresOn,
            [Globalization.CultureInfo]::InvariantCulture)
    }
    else {
        [DateTimeOffset]::UtcNow.AddMinutes(5)
    }
}

function Assert-HybridDeviceTaggerConnection {
    if ($null -eq $script:Connection) {
        throw 'No HybridDeviceTagger API connection exists. Run Connect-HybridDeviceTagger first.'
    }

    if ([string]::IsNullOrWhiteSpace($script:Connection.AccessToken) -or
        $script:Connection.ExpiresOn -le [DateTimeOffset]::UtcNow.AddMinutes(2)) {
        Initialize-HybridDeviceTaggerToken
    }
}

function Invoke-HybridDeviceTaggerRequest {
    param(
        [Parameter(Mandatory)][ValidateSet('Get', 'Put', 'Post', 'Delete')][string]$Method,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Path,
        [AllowNull()][object]$Body,
        [AllowNull()][string]$ETag,
        [switch]$IncludeResponseHeaders
    )

    Assert-HybridDeviceTaggerConnection
    $headers = @{ Authorization = "Bearer $($script:Connection.AccessToken)" }
    if (-not [string]::IsNullOrWhiteSpace($ETag)) {
        $headers['If-Match'] = $ETag
    }

    $parameters = @{
        Method = $Method
        Uri = "$($script:Connection.ApiBaseUri)/$($Path.TrimStart('/'))"
        Headers = $headers
    }
    if ($PSBoundParameters.ContainsKey('Body')) {
        $parameters.ContentType = 'application/json; charset=utf-8'
        $parameters.Body = ConvertTo-Json -InputObject $Body -Depth 20 -Compress
    }
    if ($IncludeResponseHeaders) {
        $parameters.ResponseHeadersVariable = 'responseHeaders'
    }

    try {
        $result = Invoke-RestMethod @parameters
    }
    catch {
        throw "HybridDeviceTagger API request '$Method $Path' failed: $($_.Exception.Message)"
    }

    if ($IncludeResponseHeaders) {
        return [pscustomobject]@{
            Content = $result
            ETag = [string]$responseHeaders['ETag']
        }
    }
    $result
}

function Get-HybridDeviceTaggerConfigurationSnapshot {
    Invoke-HybridDeviceTaggerRequest -Method Get -Path 'tagging-rules' -IncludeResponseHeaders
}

function Get-HybridDeviceTaggerTrustedCertificateSnapshot {
    Invoke-HybridDeviceTaggerRequest -Method Get -Path 'trusted-certificates' -IncludeResponseHeaders
}

<#
.SYNOPSIS
Connects the current PowerShell session to the HybridDeviceTagger management API.

.DESCRIPTION
Uses the existing Azure CLI sign-in to acquire a delegated Microsoft Entra access token and stores
the API URI, token audience, token, and expiry in module scope. All other module cmdlets reuse this
connection. By default, the command validates the connection by reading the tagging configuration.

.PARAMETER ApiBaseUri
Specifies the absolute HTTPS URI of the management API, including the /api/management path.

.PARAMETER ClientId
Specifies the non-secret Microsoft Entra application client ID used as the access-token audience.

.PARAMETER SkipValidation
Stores the connection without making the initial tagging-configuration request.

.PARAMETER PassThru
Returns non-sensitive connection information. The access token is never included.

.OUTPUTS
HybridDeviceTagger.Management.ConnectionInfo when PassThru is specified. Otherwise, no output.

.EXAMPLE
Connect-HybridDeviceTagger

Connects to the production management API using the existing Azure CLI sign-in.

.EXAMPLE
Connect-HybridDeviceTagger -ApiBaseUri 'https://example.azurewebsites.net/api/management' -ClientId $clientId -PassThru

Connects to another deployment and returns its non-sensitive connection information.
#>
function Connect-HybridDeviceTagger {
    [CmdletBinding()]
    param(
        [uri]$ApiBaseUri = 'https://func-hybriddevicetagger.azurewebsites.net/api/management',

        [ValidatePattern('^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$')]
        [string]$ClientId = '5e324803-5aed-4e5d-b49f-a4bfceddcda8',

        [switch]$SkipValidation,

        [switch]$PassThru
    )

    if (-not $ApiBaseUri.IsAbsoluteUri -or $ApiBaseUri.Scheme -ne 'https') {
        throw 'ApiBaseUri must be an absolute HTTPS URI.'
    }

    $script:Connection = [pscustomobject]@{
        PSTypeName = 'HybridDeviceTagger.Management.Connection'
        ApiBaseUri = $ApiBaseUri.AbsoluteUri.TrimEnd('/')
        ClientId = $ClientId
        AccessToken = $null
        ExpiresOn = [DateTimeOffset]::MinValue
    }
    try {
        Initialize-HybridDeviceTaggerToken
        if (-not $SkipValidation) {
            $null = Get-HybridDeviceTaggerConfigurationSnapshot
        }
    }
    catch {
        $script:Connection = $null
        throw
    }

    if ($PassThru) {
        [pscustomobject]@{
            PSTypeName = 'HybridDeviceTagger.Management.ConnectionInfo'
            ApiBaseUri = $script:Connection.ApiBaseUri
            ClientId = $script:Connection.ClientId
            ExpiresOn = $script:Connection.ExpiresOn
        }
    }
}

<#
.SYNOPSIS
Gets HybridDeviceTagger tagging rules.

.DESCRIPTION
Reads the effective tagging configuration through the connected management API. Without Name, the
command emits every rule. With Name, it emits rules whose names match case-insensitively.

.PARAMETER Name
Specifies an optional rule name to retrieve. Matching is case-insensitive.

.OUTPUTS
System.Management.Automation.PSCustomObject. One object is emitted for each matching rule.

.EXAMPLE
Get-HybridDeviceTaggerRule

Returns all effective tagging rules.

.EXAMPLE
Get-HybridDeviceTaggerRule -Name 'Kiosk devices'

Returns the named rule when it exists.
#>
function Get-HybridDeviceTaggerRule {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [ValidateLength(1, 128)]
        [string]$Name
    )

    $snapshot = Get-HybridDeviceTaggerConfigurationSnapshot
    $rules = @($snapshot.Content.rules)
    if ($PSBoundParameters.ContainsKey('Name')) {
        $rules | Where-Object { $_.name -ieq $Name }
    }
    else {
        $rules
    }
}

<#
.SYNOPSIS
Gets the status of the latest device-tagging run.

.DESCRIPTION
Returns the persisted state, invocation ID, timestamps, result counters, and sanitized failure
information for the latest scheduled or manually triggered device-tagging run.

.OUTPUTS
System.Management.Automation.PSCustomObject.

.EXAMPLE
Get-HybridDeviceTaggerLastRunStatus | Format-List

Displays all properties of the latest device-tagging status.
#>
function Get-HybridDeviceTaggerLastRunStatus {
    [CmdletBinding()]
    param()

    Invoke-HybridDeviceTaggerRequest -Method Get -Path 'status/device-tagging'
}

<#
.SYNOPSIS
Gets the status of the latest password-rotation operation.

.DESCRIPTION
Returns the latest password-rotation check state and the timestamp of the last actual service-account
password change. The response contains operational metadata but never contains a password.

.OUTPUTS
System.Management.Automation.PSCustomObject.

.EXAMPLE
Get-HybridDeviceTaggerPasswordRotationStatus | Format-List

Displays the latest password-rotation status and last password-change timestamp.
#>
function Get-HybridDeviceTaggerPasswordRotationStatus {
    [CmdletBinding()]
    param()

    Invoke-HybridDeviceTaggerRequest -Method Get -Path 'status/password-rotation'
}

<#
.SYNOPSIS
Gets the latest device-tagging trace as structured events.

.DESCRIPTION
Downloads and parses the latest JSONL trace. Each event is emitted as a
HybridDeviceTagger.TraceEvent object. The default screen view is one logical line in the format
time,severity,phase,status,action,details, while pipeline operations retain every event property.

.OUTPUTS
HybridDeviceTagger.TraceEvent. One object is emitted for each JSONL trace event.

.EXAMPLE
Get-HybridDeviceTaggerTrace

Displays the latest trace using the concise one-line event view.

.EXAMPLE
$errors = @(Get-HybridDeviceTaggerTrace | Where-Object level -eq 'Error')

Stores only error events as structured objects.

.EXAMPLE
Get-HybridDeviceTaggerTrace | Select-Object timestampUtc, action, details | Export-Csv ./trace.csv

Exports selected structured trace properties to a CSV file.
#>
function Get-HybridDeviceTaggerTrace {
    [CmdletBinding()]
    param()

    $content = Invoke-HybridDeviceTaggerRequest -Method Get -Path 'traces/device-tagging/latest'
    foreach ($line in $content -split "`r?`n") {
        if (-not [string]::IsNullOrWhiteSpace($line)) {
            $traceEvent = $line | ConvertFrom-Json
            $traceEvent.PSObject.TypeNames.Insert(0, 'HybridDeviceTagger.TraceEvent')
            $traceEvent
        }
    }
}

<#
.SYNOPSIS
Gets the Active Directory LDAPS trust certificates.

.DESCRIPTION
Returns non-sensitive metadata for every certificate currently trusted by new Active Directory
LDAP connections. Certificate bodies and private keys are never returned.

.OUTPUTS
System.Management.Automation.PSCustomObject. One object is emitted for each trusted certificate.

.EXAMPLE
Get-HybridDeviceTaggerTrustedCertificate

Lists each certificate thumbprint, subject, issuer, and validity period.

.EXAMPLE
Get-HybridDeviceTaggerTrustedCertificate | Where-Object notAfterUtc -lt (Get-Date).AddDays(30)

Finds trusted certificates that expire within 30 days.
#>
function Get-HybridDeviceTaggerTrustedCertificate {
    [CmdletBinding()]
    param()

    $snapshot = Get-HybridDeviceTaggerTrustedCertificateSnapshot
    @($snapshot.Content.certificates)
}

<#
.SYNOPSIS
Adds an Active Directory LDAPS trust certificate.

.DESCRIPTION
Reads one DER or PEM encoded public X.509 certificate, retrieves the latest collection ETag, and
adds the certificate through the management API. Existing certificates are preserved. Files that
contain a private key are rejected by the API.

.PARAMETER Path
Specifies the path to a DER or PEM encoded public certificate.

.OUTPUTS
System.Management.Automation.PSCustomObject representing the effective trusted-certificate collection.

.EXAMPLE
Add-HybridDeviceTaggerTrustedCertificate -Path ./contoso-root-ca.cer -WhatIf

Previews adding the public root CA certificate.

.EXAMPLE
Add-HybridDeviceTaggerTrustedCertificate -Path ./contoso-root-ca.pem

Adds the certificate while preserving all existing trust anchors.
#>
function Add-HybridDeviceTaggerTrustedCertificate {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [ValidateNotNullOrEmpty()]
        [string]$Path
    )

    process {
        $resolvedPath = Resolve-Path -LiteralPath $Path -ErrorAction Stop
        $file = Get-Item -LiteralPath $resolvedPath -ErrorAction Stop
        if ($file.PSIsContainer) {
            throw "Certificate path '$Path' must identify a file."
        }
        if ($file.Length -gt 32768) {
            throw 'The certificate file cannot exceed 32,768 bytes.'
        }

        $snapshot = Get-HybridDeviceTaggerTrustedCertificateSnapshot
        if ($PSCmdlet.ShouldProcess($file.FullName, 'Add Active Directory LDAPS trust certificate')) {
            Invoke-HybridDeviceTaggerRequest `
                -Method Post `
                -Path 'trusted-certificates' `
                -Body @{ certificate = [Convert]::ToBase64String([IO.File]::ReadAllBytes($file.FullName)) } `
                -ETag $snapshot.ETag
        }
    }
}

<#
.SYNOPSIS
Removes an Active Directory LDAPS trust certificate.

.DESCRIPTION
Retrieves the latest collection ETag and removes one certificate by SHA-1 thumbprint. The API
rejects removal of the final certificate so LDAPS cannot be left without an explicit trust anchor.

.PARAMETER Thumbprint
Specifies the 40-character SHA-1 certificate thumbprint. Spaces and separators are accepted.

.OUTPUTS
System.Management.Automation.PSCustomObject representing the remaining trusted-certificate collection.

.EXAMPLE
Remove-HybridDeviceTaggerTrustedCertificate -Thumbprint '00112233445566778899AABBCCDDEEFF00112233' -WhatIf

Previews removal of the selected trust certificate.

.EXAMPLE
Get-HybridDeviceTaggerTrustedCertificate | Where-Object thumbprint -eq $oldThumbprint |
    Remove-HybridDeviceTaggerTrustedCertificate -Confirm

Removes a certificate selected from the current collection after confirmation.
#>
function Remove-HybridDeviceTaggerTrustedCertificate {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [ValidateNotNullOrEmpty()]
        [Alias('Thumbprint')]
        [string]$CertificateThumbprint
    )

    process {
        $snapshot = Get-HybridDeviceTaggerTrustedCertificateSnapshot
        if ($PSCmdlet.ShouldProcess(
            $CertificateThumbprint,
            'Remove Active Directory LDAPS trust certificate')) {
            Invoke-HybridDeviceTaggerRequest `
                -Method Delete `
                -Path "trusted-certificates/$([uri]::EscapeDataString($CertificateThumbprint))" `
                -ETag $snapshot.ETag
        }
    }
}

<#
.SYNOPSIS
Adds or replaces one or more named HybridDeviceTagger rules.

.DESCRIPTION
Accepts either one complete rule or an object with a Rules property. Each rule is sent separately to the
management API. A rule with the same case-insensitive name is completely replaced; other rules are preserved.

.PARAMETER Rule
Specifies one complete rule object or an object whose Rules property contains up to 100 complete rules.
Each rule must contain Name, Description, BaseDn, ExtensionAttributes, EntraGroupIds, and
AdministrativeUnitIds as required by the management API.

.OUTPUTS
System.Management.Automation.PSCustomObject representing the persisted configuration when applied.

.EXAMPLE
$rule = @{
    name = 'Kiosk devices'
    description = 'Tags devices located in the kiosk OU.'
    baseDn = 'OU=Kiosks,OU=Workstations'
    extensionAttributes = @{ extensionAttribute1 = 'Kiosk' }
    entraGroupIds = @()
    administrativeUnitIds = @()
}
Add-HybridDeviceTaggerRule -Rule $rule -WhatIf

Previews adding or replacing the Kiosk devices rule.

.EXAMPLE
$rule | Add-HybridDeviceTaggerRule

Adds or replaces a complete rule received from the pipeline.

.EXAMPLE
$configuration = @{ Rules = @($kioskRule, $workstationRule) }
Add-HybridDeviceTaggerRule -Rule $configuration

Adds or replaces every rule in the Rules property while preserving all other existing rules.
#>
function Add-HybridDeviceTaggerRule {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory, ValueFromPipeline)][ValidateNotNull()][object]$Rule
    )

    process {
        $rulesProperty = $null
        if ($Rule -is [System.Collections.IDictionary]) {
            $rulesKey = @($Rule.Keys | Where-Object { [string]$_ -ieq 'Rules' })[0]
            if ($null -ne $rulesKey) {
                $rulesProperty = $Rule[$rulesKey]
            }
        }
        else {
            $property = $Rule.PSObject.Properties |
                Where-Object Name -IEQ 'Rules' |
                Select-Object -First 1
            if ($null -ne $property) {
                $rulesProperty = $property.Value
            }
        }

        $rules = if ($null -ne $rulesProperty) { @($rulesProperty) } else { @($Rule) }
        if ($rules.Count -lt 1 -or $rules.Count -gt 100) {
            throw 'Rules must contain between 1 and 100 rule objects.'
        }

        foreach ($candidateRule in $rules) {
            $name = [string]$candidateRule.name
            if ([string]::IsNullOrWhiteSpace($name)) {
                throw 'Each rule must contain a non-empty Name property.'
            }

            $snapshot = Get-HybridDeviceTaggerConfigurationSnapshot
            if ($PSCmdlet.ShouldProcess($name, 'Add or replace HybridDeviceTagger tagging rule')) {
                Invoke-HybridDeviceTaggerRequest `
                    -Method Put `
                    -Path "tagging-rules/$([uri]::EscapeDataString($name))" `
                    -Body $candidateRule `
                    -ETag $snapshot.ETag
            }
        }
    }
}

<#
.SYNOPSIS
Removes one named HybridDeviceTagger rule.

.DESCRIPTION
Reads the latest configuration ETag and removes the case-insensitively matching rule through the
management API. The API rejects a result that would leave no valid tagging rules.

.PARAMETER Name
Specifies the unique rule name to remove. Matching is case-insensitive.

.OUTPUTS
System.Management.Automation.PSCustomObject representing the persisted configuration when applied.

.EXAMPLE
Remove-HybridDeviceTaggerRule -Name 'Kiosk devices' -WhatIf

Previews removal of the named rule.

.EXAMPLE
Remove-HybridDeviceTaggerRule -Name 'Kiosk devices' -Confirm

Removes the named rule after confirmation.
#>
function Remove-HybridDeviceTaggerRule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory, Position = 0)][ValidateLength(1, 128)][string]$Name
    )

    $snapshot = Get-HybridDeviceTaggerConfigurationSnapshot
    if ($PSCmdlet.ShouldProcess($Name, 'Remove HybridDeviceTagger tagging rule')) {
        Invoke-HybridDeviceTaggerRequest `
            -Method Delete `
            -Path "tagging-rules/$([uri]::EscapeDataString($Name))" `
            -ETag $snapshot.ETag
    }
}

<#
.SYNOPSIS
Runs device tagging immediately.

.DESCRIPTION
Synchronously triggers the same device-tagging orchestration used by the timer. The command returns
the invocation ID and aggregate result. The API returns a conflict when another tagging run is active.

.OUTPUTS
System.Management.Automation.PSCustomObject containing invocationId and result.

.EXAMPLE
Invoke-HybridDeviceTagging -WhatIf

Shows the target operation without invoking device tagging.

.EXAMPLE
$result = Invoke-HybridDeviceTagging

Runs device tagging immediately and stores the invocation result.
#>
function Invoke-HybridDeviceTagging {
    [CmdletBinding(SupportsShouldProcess)]
    param()

    Assert-HybridDeviceTaggerConnection
    if ($PSCmdlet.ShouldProcess($script:Connection.ApiBaseUri, 'Run device tagging immediately')) {
        Invoke-HybridDeviceTaggerRequest -Method Post -Path 'actions/device-tagging'
    }
}

<#
.SYNOPSIS
Initializes the Active Directory service-account credential through the Function App.

.DESCRIPTION
Sends the password to the Entra-protected management API. The Function validates the credential
against Active Directory and stores it in Key Vault through its managed identity. Initialization
fails when the current credential secret already exists.

.PARAMETER Password
Specifies the initial Active Directory service-account password as a SecureString.

.OUTPUTS
System.Management.Automation.PSCustomObject confirming successful initialization.

.EXAMPLE
$password = Read-Host 'AD service-account password' -AsSecureString
Initialize-HybridDeviceTaggerActiveDirectoryCredential -Password $password -Confirm

Validates and stores the initial credential without granting the operator Key Vault data access.
#>
function Initialize-HybridDeviceTaggerActiveDirectoryCredential {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory)][ValidateNotNull()][Security.SecureString]$Password
    )

    Assert-HybridDeviceTaggerConnection
    if ($PSCmdlet.ShouldProcess(
        $script:Connection.ApiBaseUri,
        'Validate and initialize the Active Directory service-account credential')) {
        $plainPassword = ConvertFrom-SecureString -SecureString $Password -AsPlainText
        try {
            Invoke-HybridDeviceTaggerRequest `
                -Method Post `
                -Path 'actions/initialize-ad-credential' `
                -Body @{ password = $plainPassword }
        }
        finally {
            $plainPassword = $null
        }
    }
}

<#
.SYNOPSIS
Forces an immediate Active Directory service-account password rotation.

.DESCRIPTION
Synchronously triggers password rotation and bypasses only the configured password-age threshold.
Current credential verification, secure LDAP, Key Vault staging, changed-credential validation, and
secret promotion remain mandatory. The operation changes the real service-account password when safe.

.OUTPUTS
System.Management.Automation.PSCustomObject containing invocationId and result.

.EXAMPLE
Invoke-HybridDeviceTaggerPasswordRotation -WhatIf

Shows the target operation without changing the password.

.EXAMPLE
Invoke-HybridDeviceTaggerPasswordRotation -Confirm

Forces password rotation after high-impact confirmation.
#>
function Invoke-HybridDeviceTaggerPasswordRotation {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param()

    Assert-HybridDeviceTaggerConnection
    if ($PSCmdlet.ShouldProcess(
        $script:Connection.ApiBaseUri,
        'Immediately change the Active Directory service-account password')) {
        Invoke-HybridDeviceTaggerRequest -Method Post -Path 'actions/password-rotation'
    }
}

Export-ModuleMember -Function @(
    'Connect-HybridDeviceTagger',
    'Get-HybridDeviceTaggerRule',
    'Get-HybridDeviceTaggerLastRunStatus',
    'Get-HybridDeviceTaggerPasswordRotationStatus',
    'Get-HybridDeviceTaggerTrace',
    'Get-HybridDeviceTaggerTrustedCertificate',
    'Add-HybridDeviceTaggerRule',
    'Add-HybridDeviceTaggerTrustedCertificate',
    'Remove-HybridDeviceTaggerRule',
    'Remove-HybridDeviceTaggerTrustedCertificate',
    'Initialize-HybridDeviceTaggerActiveDirectoryCredential',
    'Invoke-HybridDeviceTagging',
    'Invoke-HybridDeviceTaggerPasswordRotation'
)