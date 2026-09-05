#Requires -Version 7.2
# Project-Version: 0.1.20260905.14
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

function Get-HybridDeviceTaggerLastRunStatus {
    [CmdletBinding()]
    param()

    Invoke-HybridDeviceTaggerRequest -Method Get -Path 'status/device-tagging'
}

function Get-HybridDeviceTaggerPasswordRotationStatus {
    [CmdletBinding()]
    param()

    Invoke-HybridDeviceTaggerRequest -Method Get -Path 'status/password-rotation'
}

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

function Add-HybridDeviceTaggerRule {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory, ValueFromPipeline)][ValidateNotNull()][object]$Rule
    )

    process {
        $name = [string]$Rule.name
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw 'Rule must contain a non-empty Name property.'
        }

        $snapshot = Get-HybridDeviceTaggerConfigurationSnapshot
        if ($PSCmdlet.ShouldProcess($name, 'Add or replace HybridDeviceTagger tagging rule')) {
            Invoke-HybridDeviceTaggerRequest `
                -Method Put `
                -Path "tagging-rules/$([uri]::EscapeDataString($name))" `
                -Body $Rule `
                -ETag $snapshot.ETag
        }
    }
}

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

function Invoke-HybridDeviceTagging {
    [CmdletBinding(SupportsShouldProcess)]
    param()

    Assert-HybridDeviceTaggerConnection
    if ($PSCmdlet.ShouldProcess($script:Connection.ApiBaseUri, 'Run device tagging immediately')) {
        Invoke-HybridDeviceTaggerRequest -Method Post -Path 'actions/device-tagging'
    }
}

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
    'Add-HybridDeviceTaggerRule',
    'Remove-HybridDeviceTaggerRule',
    'Set-HybridDeviceTaggerRuleConfiguration',
    'Invoke-HybridDeviceTagging',
    'Invoke-HybridDeviceTaggerPasswordRotation'
)