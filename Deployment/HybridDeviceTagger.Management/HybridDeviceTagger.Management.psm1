#Requires -Version 7.2
# Project-Version: 0.1.20261005.3
# Author: andreas.lucas@outlook.com (aka Kili)

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

<#
.SYNOPSIS
PowerShell client for the Entra-protected HybridDeviceTagger management API.

.DESCRIPTION
The module maintains one connection per PowerShell module instance. Connect-HybridDeviceTagger
initializes that connection from the current Azure CLI sign-in. All other exported commands reuse
the connection and refresh its delegated access token shortly before expiry.

Read commands return structured objects. Write and action commands use PowerShell ShouldProcess;
destructive or security-sensitive commands additionally use high-impact confirmation. Collection
updates send the latest API ETag in If-Match so concurrent changes fail instead of being overwritten.

The module does not require direct access to Active Directory, Key Vault, Blob Storage, Microsoft
Graph, or Azure Resource Manager. Those operations are performed by the Function App after the
management API has authorized the caller.

.NOTES
Requires PowerShell 7.2 or later, Azure CLI, an existing az login session, and network access to the
Function App. Importing the module does not authenticate or make a network request.

The connection and access token exist only in module scope for the current PowerShell process.
Disconnect by removing the module or closing the PowerShell session.

.LINK
https://github.com/Kili69/HybridDeviceTagger
#>

Set-StrictMode -Version Latest

# Shared only by commands in this module instance; never persisted and cleared after failed validation.
$script:Connection = $null

<#
.SYNOPSIS
Acquires or refreshes the delegated management API access token.

.DESCRIPTION
Calls Azure CLI for the api://<client-id>/.default scope and stores the token and normalized expiry
on the module-scoped connection. Both Azure CLI expiry formats are supported. If Azure CLI omits an
expiry, a conservative five-minute lifetime forces an early refresh on the next request.

.OUTPUTS
None.

.NOTES
Private module function. It requires an initialized connection and never writes the token to the
success stream.
#>
function Initialize-HybridDeviceTaggerToken {
    # Fail before accessing the connection when the external authentication dependency is unavailable.
    if (-not (Get-Command az -CommandType Application -ErrorAction SilentlyContinue)) {
        throw 'Azure CLI is required. Install Azure CLI, run az login, and try again.'
    }

    # The .default scope requests the delegated permissions already consented for this API audience.
    $tokenResponse = az account get-access-token `
        --scope "api://$($script:Connection.ClientId)/.default" `
        --output json `
        --only-show-errors | ConvertFrom-Json

    # Azure CLI failures and malformed success responses are both unusable authentication results.
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$tokenResponse.accessToken)) {
        throw "Unable to obtain an Entra access token for api://$($script:Connection.ClientId). Run 'az login --scope api://$($script:Connection.ClientId)/.default' and try again."
    }

    $script:Connection.AccessToken = [string]$tokenResponse.accessToken

    # Azure CLI versions expose either Unix seconds or a formatted date. Normalize both representations.
    $script:Connection.ExpiresOn = if ($tokenResponse.PSObject.Properties['expires_on']) {
        [DateTimeOffset]::FromUnixTimeSeconds([long]$tokenResponse.expires_on)
    }
    elseif ($tokenResponse.PSObject.Properties['expiresOn']) {
        [DateTimeOffset]::Parse(
            [string]$tokenResponse.expiresOn,
            [Globalization.CultureInfo]::InvariantCulture)
    }
    else {
        # Unknown expiry is deliberately short-lived and never creates an indefinitely valid session.
        [DateTimeOffset]::UtcNow.AddMinutes(5)
    }
}

<#
.SYNOPSIS
Ensures that a usable management API connection and access token exist.

.DESCRIPTION
Rejects commands executed before Connect-HybridDeviceTagger and refreshes the delegated token when
it is missing or expires within two minutes. The refresh margin prevents expiry during an API call.

.OUTPUTS
None.

.NOTES
Private module function.
#>
function Assert-HybridDeviceTaggerConnection {
    # Public operations cannot infer the deployment or token audience without an explicit connection.
    if ($null -eq $script:Connection) {
        throw 'No HybridDeviceTagger API connection exists. Run Connect-HybridDeviceTagger first.'
    }

    # Refresh two minutes early to cover request latency and synchronous management operations.
    if ([string]::IsNullOrWhiteSpace($script:Connection.AccessToken) -or
        $script:Connection.ExpiresOn -le [DateTimeOffset]::UtcNow.AddMinutes(2)) {
        Initialize-HybridDeviceTaggerToken
    }
}

<#
.SYNOPSIS
Sends one authenticated request to the HybridDeviceTagger management API.

.DESCRIPTION
Builds the endpoint URI from the active connection, serializes optional request bodies as JSON, adds
an optional If-Match header, and translates REST failures into terminating errors with the method and
relative path.

.PARAMETER Method
Specifies the supported HTTP method.

.PARAMETER Path
Specifies the API path relative to the configured base URI.

.PARAMETER Body
Specifies an optional object serialized as UTF-8 JSON with a maximum depth of 20.

.PARAMETER ETag
Specifies an optional entity tag used for optimistic concurrency through If-Match.

.PARAMETER IncludeResponseHeaders
Returns an envelope containing response content and the response ETag.

.OUTPUTS
The deserialized API response, or an object with Content and ETag when response headers are requested.

.NOTES
Private module function. API failures remain terminating errors so automation cannot interpret a
failed operation as success.
#>
function Invoke-HybridDeviceTaggerRequest {
    param(
        [Parameter(Mandatory)][ValidateSet('Get', 'Put', 'Post', 'Delete')][string]$Method,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Path,
        [AllowNull()][object]$Body,
        [AllowNull()][string]$ETag,
        [switch]$IncludeResponseHeaders
    )

    # Authentication state is checked for every request, not only when the connection is created.
    Assert-HybridDeviceTaggerConnection
    $headers = @{ Authorization = "Bearer $($script:Connection.AccessToken)" }
    if (-not [string]::IsNullOrWhiteSpace($ETag)) {
        # If-Match turns collection writes into optimistic-concurrency operations.
        $headers['If-Match'] = $ETag
    }

    # Normalize the relative path to one separator without changing the configured management base path.
    $parameters = @{
        Method = $Method
        Uri = "$($script:Connection.ApiBaseUri)/$($Path.TrimStart('/'))"
        Headers = $headers
    }
    if ($PSBoundParameters.ContainsKey('Body')) {
        # Depth 20 preserves nested rules, arrays, and extension-attribute dictionaries.
        $parameters.ContentType = 'application/json; charset=utf-8'
        $parameters.Body = ConvertTo-Json -InputObject $Body -Depth 20 -Compress
    }
    if ($IncludeResponseHeaders) {
        # Invoke-RestMethod assigns the response headers to this local variable after completion.
        $parameters.ResponseHeadersVariable = 'responseHeaders'
    }

    try {
        $result = Invoke-RestMethod @parameters
    }
    catch {
        # Add endpoint context and preserve a terminating failure for scripts and deployment pipelines.
        throw "HybridDeviceTagger API request '$Method $Path' failed: $($_.Exception.Message)"
    }

    if ($IncludeResponseHeaders) {
        # Snapshot callers need the resource and its version from the same HTTP response.
        return [pscustomobject]@{
            Content = $result
            ETag = [string]$responseHeaders['ETag']
        }
    }
    $result
}

<#
.SYNOPSIS
Gets the tagging configuration and its current ETag.

.OUTPUTS
An object with Content and ETag properties.

.NOTES
Private module function used immediately before ETag-protected rule changes.
#>
function Get-HybridDeviceTaggerConfigurationSnapshot {
    # Centralized snapshot acquisition ensures every rule mutation receives the current response ETag.
    Invoke-HybridDeviceTaggerRequest -Method Get -Path 'tagging-rules' -IncludeResponseHeaders
}

<#
.SYNOPSIS
Gets the trusted-certificate collection and its current ETag.

.OUTPUTS
An object with Content and ETag properties.

.NOTES
Private module function used immediately before ETag-protected certificate changes.
#>
function Get-HybridDeviceTaggerTrustedCertificateSnapshot {
    # Certificate updates use the same optimistic-concurrency contract as rule updates.
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

    # Reject relative and unencrypted endpoints before creating authentication state.
    if (-not $ApiBaseUri.IsAbsoluteUri -or $ApiBaseUri.Scheme -ne 'https') {
        throw 'ApiBaseUri must be an absolute HTTPS URI.'
    }

    # Normalize the base URI and initialize the token fields before the first acquisition.
    $script:Connection = [pscustomobject]@{
        PSTypeName = 'HybridDeviceTagger.Management.Connection'
        ApiBaseUri = $ApiBaseUri.AbsoluteUri.TrimEnd('/')
        ClientId = $ClientId
        AccessToken = $null
        ExpiresOn = [DateTimeOffset]::MinValue
    }
    try {
        # Authenticate eagerly so Connect reports authentication problems immediately.
        Initialize-HybridDeviceTaggerToken
        if (-not $SkipValidation) {
            # This read validates reachability, token audience, Easy Auth, and caller authorization.
            $null = Get-HybridDeviceTaggerConfigurationSnapshot
        }
    }
    catch {
        # A failed token request or validation read must not leave a partial connection behind.
        $script:Connection = $null
        throw
    }

    if ($PassThru) {
        # Explicit projection guarantees that the access token cannot enter the success pipeline.
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

    # The common snapshot helper also gives this read the same response-shape handling as writes.
    $snapshot = Get-HybridDeviceTaggerConfigurationSnapshot

    # Array coercion provides stable zero-, one-, or many-object pipeline behavior.
    $rules = @($snapshot.Content.rules)
    if ($PSBoundParameters.ContainsKey('Name')) {
        # Parameter presence controls filtering; ValidateLength already rejects an empty supplied name.
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

    # Return the persisted API document unchanged so future server fields remain available to callers.
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

    # The endpoint exposes operational metadata only and never returns current or staged passwords.
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

    # JSONL contains one independent JSON object per line, rather than one enclosing JSON array.
    $content = Invoke-HybridDeviceTaggerRequest -Method Get -Path 'traces/device-tagging/latest'
    foreach ($line in $content -split "`r?`n") {
        if (-not [string]::IsNullOrWhiteSpace($line)) {
            $traceEvent = $line | ConvertFrom-Json
            # Select the concise module view without flattening the object returned to the pipeline.
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

    # Discard the snapshot ETag and emit only non-sensitive certificate metadata.
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
        # Resolve LiteralPath once so wildcard characters in certificate filenames are not expanded.
        $resolvedPath = Resolve-Path -LiteralPath $Path -ErrorAction Stop
        $file = Get-Item -LiteralPath $resolvedPath -ErrorAction Stop

        # Validate local constraints before any API read or confirmation prompt.
        if ($file.PSIsContainer) {
            throw "Certificate path '$Path' must identify a file."
        }
        if ($file.Length -gt 32768) {
            throw 'The certificate file cannot exceed 32,768 bytes.'
        }

        # The approved write must target exactly the certificate collection version observed here.
        $snapshot = Get-HybridDeviceTaggerTrustedCertificateSnapshot
        if ($PSCmdlet.ShouldProcess($file.FullName, 'Add Active Directory LDAPS trust certificate')) {
            # Base64 preserves both DER bytes and PEM text bytes in the JSON transport envelope.
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

.PARAMETER CertificateThumbprint
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
        # Fetch the ETag immediately before confirmation and the conditional delete.
        $snapshot = Get-HybridDeviceTaggerTrustedCertificateSnapshot
        if ($PSCmdlet.ShouldProcess(
            $CertificateThumbprint,
            'Remove Active Directory LDAPS trust certificate')) {
            # Escape separators and whitespace accepted by the API so they remain one route segment.
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
        # Accept one rule or an exported configuration with a case-insensitive Rules property.
        $rulesProperty = $null
        if ($Rule -is [System.Collections.IDictionary]) {
            # Hashtable keys do not participate in PSObject property lookup, so inspect keys explicitly.
            $rulesKey = @($Rule.Keys | Where-Object { [string]$_ -ieq 'Rules' })[0]
            if ($null -ne $rulesKey) {
                $rulesProperty = $Rule[$rulesKey]
            }
        }
        else {
            # PSCustomObject and deserialized JSON input expose Rules through the property collection.
            $property = $Rule.PSObject.Properties |
                Where-Object Name -IEQ 'Rules' |
                Select-Object -First 1
            if ($null -ne $property) {
                $rulesProperty = $property.Value
            }
        }

        # A plain rule becomes a one-item batch; a configuration wrapper contributes its Rules collection.
        $rules = if ($null -ne $rulesProperty) { @($rulesProperty) } else { @($Rule) }
        if ($rules.Count -lt 1 -or $rules.Count -gt 100) {
            throw 'Rules must contain between 1 and 100 rule objects.'
        }

        foreach ($candidateRule in $rules) {
            # Only the routing key is checked locally; the API remains authoritative for the full schema.
            $name = [string]$candidateRule.name
            if ([string]::IsNullOrWhiteSpace($name)) {
                throw 'Each rule must contain a non-empty Name property.'
            }

            # Every successful upsert changes the collection ETag, so refresh for each rule.
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

    # Couple the delete to the configuration version presented before user confirmation.
    $snapshot = Get-HybridDeviceTaggerConfigurationSnapshot
    if ($PSCmdlet.ShouldProcess($Name, 'Remove HybridDeviceTagger tagging rule')) {
        # URI escaping keeps valid rule names with spaces or reserved characters in one route segment.
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

    # Validate before ShouldProcess so the confirmation target always refers to an active connection.
    Assert-HybridDeviceTaggerConnection
    if ($PSCmdlet.ShouldProcess($script:Connection.ApiBaseUri, 'Run device tagging immediately')) {
        # The action endpoint executes synchronously and returns its invocation ID and aggregate result.
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

    # Establish the HTTPS target before asking permission to transmit the credential.
    Assert-HybridDeviceTaggerConnection
    if ($PSCmdlet.ShouldProcess(
        $script:Connection.ApiBaseUri,
        'Validate and initialize the Active Directory service-account credential')) {
        # Plain text exists only long enough to serialize the protected HTTPS request.
        $plainPassword = ConvertFrom-SecureString -SecureString $Password -AsPlainText
        try {
            # The Function performs AD validation and Key Vault persistence with its own managed identity.
            Invoke-HybridDeviceTaggerRequest `
                -Method Post `
                -Path 'actions/initialize-ad-credential' `
                -Body @{ password = $plainPassword }
        }
        finally {
            # Remove the module's remaining reference even when serialization or the request fails.
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

    # Validate before the high-impact prompt so failures cannot occur after unnecessary confirmation.
    Assert-HybridDeviceTaggerConnection
    if ($PSCmdlet.ShouldProcess(
        $script:Connection.ApiBaseUri,
        'Immediately change the Active Directory service-account password')) {
        # Server-side safeguards still enforce LDAPS, staging, validation, and secret promotion.
        Invoke-HybridDeviceTaggerRequest -Method Post -Path 'actions/password-rotation'
    }
}

# Export only the supported management surface; authentication and HTTP helpers remain private.
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