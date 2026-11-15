#Requires -Version 7.2

<#
.SYNOPSIS
Tests deferred installer directory checks without DNS or Azure access.

.EXAMPLE
pwsh -NoProfile -File tests\InstallerDirectory.Tests.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$path = Join-Path (Split-Path $PSScriptRoot -Parent) 'installationspackage\Install-HybridDeviceTagger.ps1'
$ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }
& {
    $definition = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Wait-AzureGroupDeployment'
    }, $true)
    . ([scriptblock]::Create($definition.Extent.Text.Replace(
        '[DateTimeOffset]::UtcNow', '$script:DeploymentTestNow')))
    $SubscriptionId = 'subscription'
    $ResourceGroupName = 'resources'
    $script:DeploymentTestNow = [DateTimeOffset]::UtcNow
    $script:StatusCalls = 0
    $script:StatusWarnings = [Collections.Generic.List[string]]::new()
    $script:StatusMode = 'recover'
    function Start-Sleep {
        param($Seconds)
        $script:DeploymentTestNow = $script:DeploymentTestNow.AddSeconds($Seconds)
    }
    function Write-Warning {
        param($Message)
        $script:StatusWarnings.Add($Message)
    }
    function Get-AzureJson {
        param($Arguments, $TimeoutSeconds)
        $script:StatusCalls++
        if ($script:StatusMode -eq 'denied') { throw 'Access denied.' }
        if ($script:StatusMode -eq 'timeout' -or
            ($script:StatusMode -eq 'recover' -and $script:StatusCalls -eq 1)) {
            $script:DeploymentTestNow = $script:DeploymentTestNow.AddSeconds($TimeoutSeconds)
            throw [TimeoutException]::new('Status request timed out.')
        }
        $state = if ($script:StatusMode -eq 'recover') { 'Succeeded' } else { $script:StatusMode }
        [pscustomobject]@{ properties = [pscustomobject]@{
            provisioningState = $state
            error = @{ code = 'TestDeploymentError' }
        } }
    }
    $result = Wait-AzureGroupDeployment -DeploymentName test -TimeoutSeconds 120
    if ($result.properties.provisioningState -ne 'Succeeded' -or
        $script:StatusCalls -ne 2 -or $script:StatusWarnings.Count -ne 1) {
        throw 'A timed-out status query must warn and retry, returning the confirmed successful deployment.'
    }
    foreach ($mode in 'Failed', 'Canceled', 'denied', 'timeout') {
        $script:StatusMode = $mode
        $script:StatusCalls = 0
        $script:StatusWarnings.Clear()
        $started = $script:DeploymentTestNow
        $message = ''
        try {
            $null = Wait-AzureGroupDeployment -DeploymentName test -TimeoutSeconds 120
        }
        catch { $message = $_.Exception.Message }
        if (-not $message) { throw "Status mode '$mode' must not return success." }
        if ($mode -eq 'timeout') {
            if ($script:StatusCalls -ne 2 -or $script:StatusWarnings.Count -ne 2 -or
                ($script:DeploymentTestNow - $started).TotalSeconds -ne 120 -or
                $message -notlike '*without confirming a terminal state*') {
                throw 'Persistent status timeouts must warn and stop at the overall deadline without asserting a deployment outcome.'
            }
        }
        elseif ($script:StatusCalls -ne 1 -or $script:StatusWarnings.Count -ne 0 -or
            ($mode -in 'Failed', 'Canceled' -and $message -notlike '*TestDeploymentError*') -or
            ($mode -eq 'denied' -and $message -ne 'Access denied.')) {
            throw 'Terminal deployment errors and non-timeout query errors must propagate immediately.'
        }
    }
}
$certificateRequirement = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.IfStatementAst] -and
    $node.Clauses[0].Item1.Extent.Text -eq '[string]::IsNullOrWhiteSpace($ActiveDirectoryTrustedCertificatePath)'
}, $true)
if ($null -eq $certificateRequirement) { throw 'The engine must require a root CA certificate even when local DNS is unavailable.' }
foreach ($ActiveDirectoryTrustedCertificatePath in $null, '', '   ') {
    $failed = $false
    try {
        . ([scriptblock]::Create($certificateRequirement.Extent.Text))
    }
    catch {
        if ($_.Exception.Message -notlike '*Supply -ActiveDirectoryTrustedCertificatePath*') { throw }
        $failed = $true
    }
    if (-not $failed) { throw 'The engine must reject missing or blank root CA paths before Azure deployment.' }
}
$ActiveDirectoryTrustedCertificatePath = 'C:\Certificates\Contoso-Root-CA.cer'
. ([scriptblock]::Create($certificateRequirement.Extent.Text))

$function = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Get-ActiveDirectoryDomainControllerList'
}, $true)
. ([scriptblock]::Create($function.Extent.Text))
$discovery = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.IfStatementAst] -and
    $node.Clauses[0].Item1.Extent.Text -eq '[string]::IsNullOrWhiteSpace($ActiveDirectoryServer)' -and
    $node.Extent.Text -like '*-AllowUnavailable*'
}, $true)
function Resolve-DnsName { throw 'Internal DNS is not reachable from this installation PC.' }
$ActiveDirectoryServer = ''
$ActiveDirectoryForestDomain = 'contoso.com'
$DeferredActiveDirectoryValidation = $false
. ([scriptblock]::Create($discovery.Extent.Text))
if (-not $DeferredActiveDirectoryValidation -or $ActiveDirectoryServerCandidates.Count -ne 0) {
    throw 'Unavailable local DNS must defer AD checks without inventing a domain controller.'
}
$localValidation = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.IfStatementAst] -and
    $node.Clauses[0].Item1.Extent.Text -eq '-not $SkipActiveDirectoryDirectoryChecks -and -not $script:DeferredActiveDirectoryValidation'
}, $true)
$SkipActiveDirectoryDirectoryChecks = $false
function Resolve-ADServiceAccountDistinguishedName { throw 'Local LDAP must not run when DNS was unavailable.' }
. ([scriptblock]::Create($localValidation.Extent.Text))

function Resolve-DnsName {
    @(
        [pscustomobject]@{ NameTarget = 'dc02.contoso.com.'; Priority = 10; Weight = 0 }
        [pscustomobject]@{ NameTarget = 'dc01.contoso.com.'; Priority = 0; Weight = 100 }
    )
}
. ([scriptblock]::Create($discovery.Extent.Text))
if ($ActiveDirectoryServer -ne 'dc01.contoso.com' -or
    $DeferredActiveDirectoryValidation -or $ActiveDirectoryServerCandidates.Count -ne 2) {
    throw 'Available local DNS must retain ordered discovery and local validation.'
}

function Resolve-DnsName { @() }
$candidates = @(Get-ActiveDirectoryDomainControllerList -Domain contoso.com -AllowUnavailable)
if ($candidates.Count -ne 0) { throw 'Empty SRV results must defer checks without returning a fake server.' }
$failed = $false
try {
    Get-ActiveDirectoryDomainControllerList -Domain contoso.com
}
catch {
    $failed = $true
}
if (-not $failed) { throw 'Strict discovery used by directory validation must still fail on missing SRV records.' }
Write-Host 'Installer directory tests passed.'

foreach ($name in 'Get-PrivateEndpointAddress', 'Test-CustomDnsPrivateEndpointResolution',
    'Write-PrivateEndpointDnsGuidance', 'Assert-PrivateEndpointDnsResolution') {
    $definition = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    . ([scriptblock]::Create($definition.Extent.Text))
}

$SubscriptionId = 'subscription'
$ResourceGroupName = 'resources'
$KeyVaultSubscriptionId = 'subscription'
$KeyVaultResourceGroupName = 'resources'
$StorageAccountName = 'teststorage'
$KeyVaultName = 'testvault'
$CreateKeyVault = $true
$NonInteractive = $false
$virtualNetwork = [pscustomobject]@{
    dhcpOptions = [pscustomobject]@{ dnsServers = @('10.0.1.4', '10.0.1.5') }
}
$Addresses = @{
    'teststorage.blob.core.windows.net' = '10.0.3.8'
    'teststorage.queue.core.windows.net' = '10.0.3.9'
    'testvault.vault.azure.net' = '10.0.3.10'
}
$Messages = [Collections.Generic.List[string]]::new()
$Warnings = [Collections.Generic.List[string]]::new()
$Queries = [Collections.Generic.List[object]]::new()
$DnsRepaired = $false
$ConfirmationAction = ''
$PromptCount = 0
$AzureReadCount = 0
$FailureMode = 'publicAddress'

function Write-Host {
    param([object]$Object, $ForegroundColor)
    $script:Messages.Add([string]$Object)
}
function Write-Warning {
    param([string]$Message)
    $script:Warnings.Add($Message)
}
function Read-Host {
    param($Prompt)
    $script:PromptCount++
    $script:ConfirmationAction
}
function Get-AzureJson {
    param([string[]]$Arguments)
    $script:AzureReadCount++
    if ($Arguments[0] -eq 'storage') {
        return [pscustomobject]@{ primaryEndpoints = [pscustomobject]@{
            blob = 'https://teststorage.blob.core.windows.net/'
            queue = 'https://teststorage.queue.core.windows.net/'
        } }
    }
    if ($Arguments[0] -eq 'keyvault') {
        return [pscustomobject]@{ properties = [pscustomobject]@{ vaultUri = 'https://testvault.vault.azure.net/' } }
    }
    if ($Arguments[1] -eq 'private-endpoint') {
        $name = $Arguments[[Array]::IndexOf($Arguments, '--name') + 1]
        return [pscustomobject]@{ networkInterfaces = @([pscustomobject]@{ id = $name }) }
    }
    if ($Arguments[1] -eq 'nic') {
        $id = $Arguments[[Array]::IndexOf($Arguments, '--ids') + 1]
        $address = switch ($id) {
            'pe-teststorage-blob' { $script:Addresses['teststorage.blob.core.windows.net'] }
            'pe-teststorage-queue' { $script:Addresses['teststorage.queue.core.windows.net'] }
            'pe-testvault' { $script:Addresses['testvault.vault.azure.net'] }
            default { throw "Unexpected NIC: $id" }
        }
        return [pscustomobject]@{ ipConfigurations = @([pscustomobject]@{ privateIPAddress = $address }) }
    }
    throw 'Unexpected Azure query in the DNS test.'
}
function Resolve-DnsName {
    [CmdletBinding()]
    param($Name, $Server, $Type, [switch]$DnsOnly)
    $script:Queries.Add([pscustomobject]@{ Name = $Name; Server = $Server })
    if ($script:DnsRepaired -or $Server -eq '10.0.1.5') {
        return @(
            [pscustomobject]@{ NameTarget = 'alias-record-without-IP' }
            [pscustomobject]@{ IPAddress = $script:Addresses[$Name] }
        )
    }
    if ($script:FailureMode -eq 'unreachable') {
        Write-Error 'DNS server is unreachable.'
        return
    }
    [pscustomobject]@{ IPAddress = '52.0.0.1' }
}
function Assert-DnsFailure {
    param([string]$Expected)
    try { Assert-PrivateEndpointDnsResolution }
    catch {
        if ($_.Exception.Message -like $Expected) { return }
        throw
    }
    throw "Expected a DNS failure matching '$Expected'."
}

Assert-PrivateEndpointDnsResolution
foreach ($record in @(
    'teststorage.privatelink.blob.core.windows.net. 300 IN A 10.0.3.8'
    'teststorage.privatelink.queue.core.windows.net. 300 IN A 10.0.3.9'
    'testvault.privatelink.vaultcore.azure.net. 300 IN A 10.0.3.10'
)) {
    if ($Messages -notcontains $record) { throw "Missing concrete zone record: $record" }
}
if ($PromptCount -ne 1 -or $Queries.Count -ne 6 -or $DnsRepaired -or
    @($Messages | Where-Object { $_ -like 'Private endpoint DNS resolution validated*' }).Count -ne 0 -or
    @($Warnings | Where-Object { $_ -like '*Continuing based on user-confirmed DNS*WITHOUT rechecking*' }).Count -ne 1) {
    throw 'Enter must continue after one DNS query per endpoint/server, without rechecking or claiming successful validation.'
}

$FailureMode = 'unreachable'
$PromptCount = 0
$Messages.Clear()
$Warnings.Clear()
$Queries.Clear()
Assert-PrivateEndpointDnsResolution
if ($PromptCount -ne 1 -or $Queries.Count -ne 6 -or
    @($Warnings | Where-Object { $_ -like '*DNS server is unreachable*' }).Count -ne 3 -or
    @($Warnings | Where-Object { $_ -like '*Continuing based on user-confirmed DNS*' }).Count -ne 1 -or
    @($Messages | Where-Object { $_ -like 'Private endpoint DNS resolution validated*' }).Count -ne 0) {
    throw 'Enter must also continue without rechecking when the DNS server is unreachable from the PC.'
}

$DnsRepaired = $false
$ConfirmationAction = 'A'
$Messages.Clear()
Assert-DnsFailure -Expected 'Installation aborted because private endpoint DNS*'
if (@($Messages | Where-Object { $_ -like 'Private endpoint DNS resolution validated*' }).Count -ne 0) {
    throw 'Aborting DNS repair must not report successful validation.'
}

$NonInteractive = $true
$FailureMode = 'unreachable'
$PromptCount = 0
$Warnings.Clear()
Assert-DnsFailure -Expected 'Private endpoint DNS validation failed*'
if ($PromptCount -ne 0 -or
    @($Warnings | Where-Object { $_ -like '*DNS server is unreachable*' }).Count -ne 3) {
    throw 'Unattended failures must report DNS errors without prompting or silently passing.'
}

$NonInteractive = $false
$DnsRepaired = $true
$CreateKeyVault = $false
$Messages.Clear()
$Queries.Clear()
Assert-PrivateEndpointDnsResolution
if ($PromptCount -ne 0 -or $Queries.Count -ne 4 -or
    @($Messages | Where-Object { $_ -like 'Zone:*' }).Count -ne 0) {
    throw 'Working Blob and Queue DNS must pass without repair prompts or new-vault checks.'
}
$virtualNetwork.dhcpOptions.dnsServers = @()
$AzureReadCount = 0
Assert-PrivateEndpointDnsResolution
if ($AzureReadCount -ne 0) { throw 'Azure-provided DNS must retain the existing no-custom-DNS path.' }
Write-Output 'Private endpoint DNS confirmation tests passed.'
