#Requires -Version 7.2
# Project-Version: 0.1.20261005.1
# Author: andreas.lucas@outlook.com (aka Kili)

<#
DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
#>

Set-StrictMode -Version Latest
$script:Connection = $null

function Get-HybridDeviceTaggerTokenScope {
    param([Parameter(Mandatory)][uri]$ApiBaseUri)

    try {
        $metadata = Invoke-RestMethod `
            -Method Get `
            -Uri "$($ApiBaseUri.AbsoluteUri.TrimEnd('/'))/authentication"
    }
    catch {
        throw "Unable to read HybridDeviceTagger authentication metadata: $($_.Exception.Message)"
    }

    $tokenScope = [string]$metadata.tokenScope
    if ($tokenScope -notmatch '^api://[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}/\.default$') {
        throw 'HybridDeviceTagger authentication metadata returned an invalid token scope.'
    }

    $tokenScope
}

function Initialize-HybridDeviceTaggerToken {
    if (-not (Get-Command az -CommandType Application -ErrorAction SilentlyContinue)) {
        throw 'Azure CLI is required. Install Azure CLI, run az login, and try again.'
    }

    $tokenResponse = az account get-access-token `
        --scope $script:Connection.TokenScope `
        --output json `
        --only-show-errors | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$tokenResponse.accessToken)) {
        throw "Unable to obtain an Entra access token for $($script:Connection.TokenScope). Run 'az login --scope $($script:Connection.TokenScope)' and try again."
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
the API URI, REST-discovered token scope, token, and expiry in module scope. All other module cmdlets
reuse this connection. By default, the command validates it by reading the tagging configuration.

.PARAMETER ApiBaseUri
Specifies the absolute HTTPS URI of the management API, including the /api/management path.

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
Connect-HybridDeviceTagger -ApiBaseUri 'https://example.azurewebsites.net/api/management' -PassThru

Connects to another deployment and returns its non-sensitive connection information.
#>
function Connect-HybridDeviceTagger {
    [CmdletBinding()]
    param(
        [uri]$ApiBaseUri = 'https://func-hybriddevicetagger.azurewebsites.net/api/management',

        [switch]$SkipValidation,

        [switch]$PassThru
    )

    if (-not $ApiBaseUri.IsAbsoluteUri -or $ApiBaseUri.Scheme -ne 'https') {
        throw 'ApiBaseUri must be an absolute HTTPS URI.'
    }

    $script:Connection = [pscustomobject]@{
        PSTypeName = 'HybridDeviceTagger.Management.Connection'
        ApiBaseUri = $ApiBaseUri.AbsoluteUri.TrimEnd('/')
        TokenScope = Get-HybridDeviceTaggerTokenScope -ApiBaseUri $ApiBaseUri
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
Replaces the complete HybridDeviceTagger tagging-rule configuration.

.DESCRIPTION
Reads the latest configuration ETag and atomically replaces all rules through the management API.
Rules omitted from Rule are removed. The API validates the complete configuration before persisting it.

.PARAMETER Rule
Specifies the complete desired rule set. At least one and at most 100 rules are required.

.OUTPUTS
System.Management.Automation.PSCustomObject representing the persisted configuration when applied.

.EXAMPLE
$rules = @(Get-HybridDeviceTaggerRule)
Set-HybridDeviceTaggerRuleConfiguration -Rule $rules -WhatIf

Previews replacement of the complete configuration without changing it.

.EXAMPLE
Set-HybridDeviceTaggerRuleConfiguration -Rule $rules -Confirm

Replaces the complete configuration after confirmation.
#>
function Set-HybridDeviceTaggerRuleConfiguration {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory)][ValidateNotNull()][ValidateCount(1, 100)][object[]]$Rule
    )

    $snapshot = Get-HybridDeviceTaggerConfigurationSnapshot
    if ($PSCmdlet.ShouldProcess($script:Connection.ApiBaseUri, 'Replace all HybridDeviceTagger tagging rules')) {
        Invoke-HybridDeviceTaggerRequest `
            -Method Put `
            -Path 'tagging-rules' `
            -Body $Rule `
            -ETag $snapshot.ETag
    }
}

function Read-HybridDeviceTaggerRequiredValue {
    param([Parameter(Mandatory)][string]$Prompt)

    do {
        $value = Read-Host $Prompt
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }

        Write-Host 'A value is required.' -ForegroundColor Yellow
    } while ($true)
}

function Read-HybridDeviceTaggerObjectIds {
    param([Parameter(Mandatory)][string]$Prompt)

    do {
        $inputValue = Read-Host "$Prompt (comma-separated, Enter for none)"
        if ([string]::IsNullOrWhiteSpace($inputValue)) {
            return @()
        }

        $objectIds = @($inputValue.Split(',') | ForEach-Object { $_.Trim() })
        $invalidObjectIds = @($objectIds | Where-Object {
            $parsedObjectId = [guid]::Empty
            -not [guid]::TryParse($_, [ref]$parsedObjectId) -or
                $parsedObjectId -eq [guid]::Empty
        })
        if ($invalidObjectIds.Count -eq 0 -and
            @($objectIds | Select-Object -Unique).Count -eq $objectIds.Count) {
            return $objectIds
        }

        Write-Host 'Enter unique, nonempty GUIDs separated by commas.' -ForegroundColor Yellow
    } while ($true)
}

function Read-HybridDeviceTaggerRule {
    $name = Read-HybridDeviceTaggerRequiredValue -Prompt 'Rule name'
    $description = Read-HybridDeviceTaggerRequiredValue -Prompt 'Rule description'
    $baseDn = Read-HybridDeviceTaggerRequiredValue -Prompt 'Active Directory base DN'

    do {
        $extensionAttributes = [System.Collections.Generic.Dictionary[string, string]]::new(
            [StringComparer]::OrdinalIgnoreCase
        )
        do {
            $attributeName = (Read-Host 'Extension attribute name (extensionAttribute1-15, Enter to finish)').Trim()
            if ([string]::IsNullOrWhiteSpace($attributeName)) {
                break
            }
            if ($attributeName -notmatch '^extensionAttribute(?:[1-9]|1[0-5])$') {
                Write-Host 'Use extensionAttribute1 through extensionAttribute15.' -ForegroundColor Yellow
                continue
            }
            if ($extensionAttributes.ContainsKey($attributeName)) {
                Write-Host "Attribute '$attributeName' was already entered." -ForegroundColor Yellow
                continue
            }

            $extensionAttributes[$attributeName] = Read-HybridDeviceTaggerRequiredValue `
                -Prompt "Value for $attributeName"
        } while ($true)

        $entraGroupIds = @(Read-HybridDeviceTaggerObjectIds -Prompt 'Entra security-group object IDs')
        $administrativeUnitIds = @(Read-HybridDeviceTaggerObjectIds -Prompt 'Administrative-unit object IDs')
        if ($extensionAttributes.Count -eq 0 -and
            $entraGroupIds.Count -eq 0 -and
            $administrativeUnitIds.Count -eq 0) {
            Write-Host 'Configure at least one extension attribute, Entra group, or administrative unit.' -ForegroundColor Yellow
        }
    } while ($extensionAttributes.Count -eq 0 -and
        $entraGroupIds.Count -eq 0 -and
        $administrativeUnitIds.Count -eq 0)

    [ordered]@{
        name = $name
        description = $description
        baseDn = $baseDn
        extensionAttributes = $extensionAttributes
        entraGroupIds = $entraGroupIds
        administrativeUnitIds = $administrativeUnitIds
    }
}

<#
.SYNOPSIS
Adds or replaces one named HybridDeviceTagger rule.

.DESCRIPTION
Reads the latest configuration ETag and sends one complete rule to the management API. If a rule with
the same case-insensitive name exists, that rule is completely replaced. Other rules are preserved.
Without Rule, the cmdlet interactively collects all rule fields and actions.

.PARAMETER Rule
Specifies one complete rule object. It must contain Name, Description, BaseDn, ExtensionAttributes,
EntraGroupIds, and AdministrativeUnitIds as required by the management API.

.PARAMETER Interactive
Starts guided rule configuration. This is the default when Rule is omitted.

.OUTPUTS
System.Management.Automation.PSCustomObject representing the persisted configuration when applied.

.EXAMPLE
Add-HybridDeviceTaggerRule

Interactively collects and persists a complete tagging rule.

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
#>
function Add-HybridDeviceTaggerRule {
    [CmdletBinding(SupportsShouldProcess, DefaultParameterSetName = 'Interactive')]
    param(
        [Parameter(Mandatory, ValueFromPipeline, ParameterSetName = 'Rule')]
        [ValidateNotNull()]
        [object]$Rule,

        [Parameter(ParameterSetName = 'Interactive')]
        [switch]$Interactive
    )

    process {
        $effectiveRule = if ($PSCmdlet.ParameterSetName -eq 'Rule') {
            $Rule
        }
        else {
            Read-HybridDeviceTaggerRule
        }
        $name = [string]$effectiveRule.name
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw 'Rule must contain a non-empty Name property.'
        }

        $snapshot = Get-HybridDeviceTaggerConfigurationSnapshot
        if ($PSCmdlet.ShouldProcess($name, 'Add or replace HybridDeviceTagger tagging rule')) {
            Invoke-HybridDeviceTaggerRequest `
                -Method Put `
                -Path "tagging-rules/$([uri]::EscapeDataString($name))" `
                -Body $effectiveRule `
                -ETag $snapshot.ETag
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
    'Set-HybridDeviceTaggerRuleConfiguration',
    'Invoke-HybridDeviceTagging',
    'Invoke-HybridDeviceTaggerPasswordRotation'
)