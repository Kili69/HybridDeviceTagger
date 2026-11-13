#Requires -Version 7.2
# Project-Version: source
# Author: andreas.lucas@outlook.com (aka Kili)

<#
DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
#>

Set-StrictMode -Version Latest
# Module-scoped connection created by Connect-HybridDeviceTagger and reused by all cmdlets.
$script:Connection = $null

<#
.SYNOPSIS
Discovers the Entra token scope of a management API deployment.

.DESCRIPTION
Reads the anonymous /authentication metadata endpoint and requires a scope in the form
api://<client ID>/.default.

.PARAMETER ApiBaseUri
Absolute URI of the management API, including the /api/management path.

.OUTPUTS
System.String
#>
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

<#
.SYNOPSIS
Acquires a delegated access token for the connected management API through the Azure CLI.

.DESCRIPTION
Stores the token and its expiry in the module-scoped connection. The expiry is read from expires_on
or expiresOn; when neither is returned, the token is treated as valid for five minutes.
#>
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

<#
.SYNOPSIS
Ensures that a connection exists and that its access token is valid for at least two minutes.

.DESCRIPTION
Throws when Connect-HybridDeviceTagger has not been run. Refreshes a missing or expiring token.
#>
function Assert-HybridDeviceTaggerConnection {
    if ($null -eq $script:Connection) {
        throw 'No HybridDeviceTagger API connection exists. Run Connect-HybridDeviceTagger first.'
    }

    if ([string]::IsNullOrWhiteSpace($script:Connection.AccessToken) -or
        $script:Connection.ExpiresOn -le [DateTimeOffset]::UtcNow.AddMinutes(2)) {
        Initialize-HybridDeviceTaggerToken
    }
}

<#
.SYNOPSIS
Sends an authenticated request to the connected management API.

.DESCRIPTION
Adds the bearer token, an optional If-Match header for optimistic concurrency, and a compact JSON
body when Body is bound. Request failures are rethrown with the method and path.

.PARAMETER Method
HTTP method: Get, Put, Post, or Delete.

.PARAMETER Path
Path relative to the management API base URI.

.PARAMETER Body
Object serialized as the JSON request body. The body is sent only when this parameter is bound.

.PARAMETER ETag
Configuration ETag sent in the If-Match header when not empty.

.PARAMETER IncludeResponseHeaders
Returns the response content together with the response ETag.

.OUTPUTS
The deserialized response, or a PSCustomObject with Content and ETag properties when
IncludeResponseHeaders is specified.
#>
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

<#
.SYNOPSIS
Reads the current tagging-rule configuration together with its ETag.

.OUTPUTS
PSCustomObject with Content (the configuration) and ETag properties.
#>
function Get-HybridDeviceTaggerConfigurationSnapshot {
    Invoke-HybridDeviceTaggerRequest -Method Get -Path 'tagging-rules' -IncludeResponseHeaders
}

<#
.SYNOPSIS
Polls an operation status every 10 seconds until the requested invocation finishes.

.DESCRIPTION
Returns when the state is Succeeded or Failed. Throws when a different invocation has replaced the
requested one in the status endpoint or when the timeout elapses.

.PARAMETER Operation
Operation to poll: device-tagging or password-rotation.

.PARAMETER InvocationId
Invocation ID returned when the operation was queued.

.PARAMETER TimeoutSeconds
Maximum time to wait before failing.

.OUTPUTS
System.Management.Automation.PSCustomObject. The final operation status.
#>
function Wait-HybridDeviceTaggerOperation {
    param(
        [Parameter(Mandatory)][ValidateSet('device-tagging', 'password-rotation')][string]$Operation,
        [Parameter(Mandatory)][string]$InvocationId,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        $status = Invoke-HybridDeviceTaggerRequest -Method Get -Path "status/$Operation"
        if ([string]$status.invocationId -ne $InvocationId) {
            throw "The $Operation request '$InvocationId' was superseded by invocation '$($status.invocationId)'. Check the status and retry if required."
        }
        if ([string]$status.state -in @('Succeeded', 'Failed')) {
            return $status
        }
        if ([DateTimeOffset]::UtcNow -ge $deadline) {
            throw "The $Operation request '$InvocationId' did not finish within $TimeoutSeconds seconds. Its current state is '$($status.state)'."
        }
        Start-Sleep -Seconds 10
    }
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

<#
.SYNOPSIS
Prompts until a nonempty value is entered.

.PARAMETER Prompt
Prompt text displayed to the user.

.OUTPUTS
System.String. The trimmed value.
#>
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

<#
.SYNOPSIS
Prompts for an optional comma-separated list of Entra object IDs.

.DESCRIPTION
Empty input returns an empty array. Input is requested again until all entries are unique,
nonempty GUIDs.

.PARAMETER Prompt
Prompt text displayed to the user.

.OUTPUTS
System.String[]
#>
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

<#
.SYNOPSIS
Interactively collects a complete tagging rule.

.DESCRIPTION
Prompts for the name, description, and base DN, then for extensionAttribute1-15 values, Entra group
IDs, and administrative-unit IDs. Repeats the action prompts until at least one action is configured.

.OUTPUTS
System.Collections.Specialized.OrderedDictionary in the management API rule format.
#>
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
Starts device tagging immediately.

.DESCRIPTION
Queues the same device-tagging orchestration used by the timer and returns the accepted request
with its invocation ID. The API returns a conflict when another tagging run is active. Use Wait to
poll the status endpoint until the requested run has finished.

.PARAMETER Wait
Waits for the queued run to finish and returns its final status.

.PARAMETER TimeoutSeconds
Specifies how long Wait polls before failing. The default is 1,800 seconds.

.OUTPUTS
System.Management.Automation.PSCustomObject containing invocationId, state, and statusUri, or the
final operation status when Wait is specified.

.EXAMPLE
Invoke-HybridDeviceTagging -WhatIf

Shows the target operation without queuing device tagging.

.EXAMPLE
$status = Invoke-HybridDeviceTagging -Wait

Runs device tagging and returns the final status including the aggregate result.
#>
function Invoke-HybridDeviceTagging {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [switch]$Wait,

        [ValidateRange(30, 86400)]
        [int]$TimeoutSeconds = 1800
    )

    Assert-HybridDeviceTaggerConnection
    if ($PSCmdlet.ShouldProcess($script:Connection.ApiBaseUri, 'Run device tagging immediately')) {
        $accepted = Invoke-HybridDeviceTaggerRequest -Method Post -Path 'actions/device-tagging'
        if (-not $Wait) {
            return $accepted
        }
        Wait-HybridDeviceTaggerOperation `
            -Operation 'device-tagging' `
            -InvocationId ([string]$accepted.invocationId) `
            -TimeoutSeconds $TimeoutSeconds
    }
}

<#
.SYNOPSIS
Forces an immediate Active Directory service-account password rotation.

.DESCRIPTION
Queues a password rotation that bypasses only the configured password-age threshold. Current
credential verification, secure LDAP, Key Vault staging, changed-credential validation, and secret
promotion remain mandatory. The operation changes the real service-account password when safe.
Use Wait to poll the status endpoint until the requested rotation has finished.

.PARAMETER Wait
Waits for the queued rotation to finish and returns its final status.

.PARAMETER TimeoutSeconds
Specifies how long Wait polls before failing. The default is 600 seconds.

.OUTPUTS
System.Management.Automation.PSCustomObject containing invocationId, state, and statusUri, or the
final operation status when Wait is specified.

.EXAMPLE
Invoke-HybridDeviceTaggerPasswordRotation -WhatIf

Shows the target operation without changing the password.

.EXAMPLE
Invoke-HybridDeviceTaggerPasswordRotation -Wait -Confirm

Forces password rotation after high-impact confirmation and waits for the result.
#>
function Invoke-HybridDeviceTaggerPasswordRotation {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [switch]$Wait,

        [ValidateRange(30, 86400)]
        [int]$TimeoutSeconds = 600
    )

    Assert-HybridDeviceTaggerConnection
    if ($PSCmdlet.ShouldProcess(
        $script:Connection.ApiBaseUri,
        'Immediately change the Active Directory service-account password')) {
        $accepted = Invoke-HybridDeviceTaggerRequest -Method Post -Path 'actions/password-rotation'
        if (-not $Wait) {
            return $accepted
        }
        Wait-HybridDeviceTaggerOperation `
            -Operation 'password-rotation' `
            -InvocationId ([string]$accepted.invocationId) `
            -TimeoutSeconds $TimeoutSeconds
    }
}

Export-ModuleMember -Function @(
    'Connect-HybridDeviceTagger',
    'Get-HybridDeviceTaggerRule',
    'Get-HybridDeviceTaggerLastRunStatus',
    'Get-HybridDeviceTaggerPasswordRotationStatus',
    'Get-HybridDeviceTaggerTrace',
    'Add-HybridDeviceTaggerRule',
    'Remove-HybridDeviceTaggerRule',
    'Set-HybridDeviceTaggerRuleConfiguration',
    'Invoke-HybridDeviceTagging',
    'Invoke-HybridDeviceTaggerPasswordRotation'
)