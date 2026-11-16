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
$installerText = [IO.File]::ReadAllText($path)
$preseedStart = $installerText.IndexOf('# Active Directory values are optional pre-seeding only')
$preseedEnd = $installerText.IndexOf('# Phase 1:', $preseedStart)
if ($preseedStart -lt 0 -or $preseedEnd -lt 0) { throw 'The installer must validate optional Active Directory pre-seeding before Azure access.' }
$preseedValidation = [scriptblock]::Create(
    'param($ActiveDirectoryForestDomain, $ADServiceAccount, $ADServiceAccountCredential, $ActiveDirectoryBindUserDistinguishedName, $ActiveDirectoryServer, [switch]$SkipActiveDirectoryDirectoryChecks, $ActiveDirectoryTrustedCertificatePath)' +
    [Environment]::NewLine + $installerText.Substring($preseedStart, $preseedEnd - $preseedStart) +
    [Environment]::NewLine + '$script:ConfigureActiveDirectory')
$testCredential = [pscredential]::new('svc@contoso.com', (ConvertTo-SecureString 'x' -AsPlainText -Force))
if (& $preseedValidation) { throw 'An installation without Active Directory parameters must not configure Active Directory.' }
foreach ($case in @(
    @{ Parameters = @{ ADServiceAccount = 'svc@contoso.com' }; Expected = '*require -ActiveDirectoryForestDomain*' }
    @{ Parameters = @{ ADServiceAccountCredential = $testCredential }; Expected = '*require -ActiveDirectoryForestDomain*' }
    @{ Parameters = @{ ActiveDirectoryForestDomain = 'contoso.com'; ADServiceAccountCredential = $testCredential }; Expected = '*requires -ActiveDirectoryTrustedCertificatePath*' }
)) {
    $parameters = $case.Parameters
    $message = ''
    try { $null = & $preseedValidation @parameters } catch { $message = $_.Exception.Message }
    if ($message -notlike $case.Expected) { throw "Inconsistent pre-seeding must fail with '$($case.Expected)', received '$message'." }
}
if (-not (& $preseedValidation -ActiveDirectoryForestDomain contoso.com -ADServiceAccountCredential $testCredential -ActiveDirectoryTrustedCertificatePath 'C:\Root.cer') -or
    -not (& $preseedValidation -ActiveDirectoryForestDomain contoso.com)) {
    throw 'A forest domain alone or with a credential and certificate must pre-seed Active Directory.'
}
if ($installerText -match '(?m)^#Requires -Modules ActiveDirectory' -or $installerText -match '(?m)^\s*\$\w+ = Read-Host .*Active Directory') {
    throw 'The installer must neither require the ActiveDirectory module nor prompt for Active Directory values.'
}
$ActiveDirectoryTrustedCertificatePath = 'C:\Certificates\Contoso-Root-CA.cer'

# Validated parameters keep their attributes; adopting missing app settings must not assign ''.
$adoptStart = $installerText.IndexOf('if (-not $script:ConfigureActiveDirectory) {', $installerText.IndexOf('# Updates without pre-seeded values'))
$adoptEnd = $installerText.IndexOf('$script:DroppedPackagedCertificate', $adoptStart)
$adoptSettings = [scriptblock]::Create(
    'param([ValidatePattern(''^(?=.{1,253}$)([a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$'')][string]$ActiveDirectoryForestDomain, [ValidateNotNullOrEmpty()][string]$ADServiceAccount, [ValidateNotNullOrEmpty()][string]$ActiveDirectoryBindUserDistinguishedName)' +
    [Environment]::NewLine + '$script:ConfigureActiveDirectory = $false; function Write-Host {}' + [Environment]::NewLine +
    $installerText.Substring($adoptStart, $adoptEnd - $adoptStart) + [Environment]::NewLine +
    '"$ActiveDirectoryForestDomain|$ADServiceAccount"')
$script:ExistingAppSettings = @{}
if ((& $adoptSettings) -ne '|') { throw 'A new installation must start without Active Directory settings.' }
$script:ExistingAppSettings = @{ 'ActiveDirectory__Domain' = 'contoso.com'; 'ActiveDirectory__BindUsername' = 'svc@contoso.com'; 'ActiveDirectory__BindUserDistinguishedName' = '' }
if ((& $adoptSettings) -ne 'contoso.com|svc@contoso.com') { throw 'Updates must keep the existing Active Directory settings.' }
foreach ($name in 'Get-SubnetServiceEndpointWarning', 'Assert-Subnet') {
    $definition = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    . ([scriptblock]::Create($definition.Extent.Text))
}
$Location = 'westus2'
$SubnetName = 'snet-fc1'
$fc1Subnet = [pscustomobject]@{
    addressPrefix = '10.0.2.0/27'
    delegations = @([pscustomobject]@{ serviceName = 'Microsoft.App/environments' })
    serviceEndpoints = @(
        [pscustomobject]@{ service = 'Microsoft.Storage'; provisioningState = 'Succeeded'; locations = @('westus2', 'westcentralus') }
        [pscustomobject]@{ service = 'Microsoft.KeyVault'; provisioningState = 'Updating'; locations = @('*') }
        [pscustomobject]@{ service = 'Microsoft.Sql'; provisioningState = 'Succeeded'; locations = @('northeurope') }
    )
}
$endpointWarnings = @(Get-SubnetServiceEndpointWarning -Subnet $fc1Subnet -SubnetLabel 'FC1 subnet')
if ($endpointWarnings.Count -ne 3 -or
    $endpointWarnings[0] -notlike "*'Microsoft.Storage'*" -or $endpointWarnings[0] -like '*Findings*' -or
    $endpointWarnings[1] -notlike "*provisioning state is 'Updating'*" -or
    $endpointWarnings[2] -notlike "*do not include 'westus2'*") {
    throw 'Service endpoints must be reported with provisioning state and region coverage.'
}
$subnetWarnings = [Collections.Generic.List[string]]::new()
function Write-Warning { param([string]$Message) $subnetWarnings.Add($Message) }
Assert-Subnet -Subnet $fc1Subnet
if ($subnetWarnings.Count -ne 3) { throw 'Service endpoints on the FC1 subnet must warn instead of failing.' }
$fc1Subnet.serviceEndpoints = @()
$subnetWarnings.Clear()
Assert-Subnet -Subnet $fc1Subnet
if ($subnetWarnings.Count -ne 0) { throw 'A subnet without service endpoints must not warn.' }
Remove-Item Function:\Write-Warning

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
    $node.Clauses[0].Item1.Extent.Text -like '$script:ConfigureActiveDirectory -and $null -ne $ADServiceAccountCredential -and*-not $SkipActiveDirectoryDirectoryChecks -and -not $script:DeferredActiveDirectoryValidation'
}, $true)
if ($null -eq $localValidation) { throw 'The local service-account validation block was not found.' }
$ConfigureActiveDirectory = $true
$ADServiceAccountCredential = $testCredential
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
if ($PromptCount -ne 0 -or $Queries.Count -ne 6 -or
    @($Warnings | Where-Object { $_ -like '*cannot be queried from this computer*' }).Count -ne 1 -or
    $Messages -notcontains 'teststorage.privatelink.blob.core.windows.net. 300 IN A 10.0.3.8' -or
    @($Messages | Where-Object { $_ -like 'Private endpoint DNS resolution validated*' }).Count -ne 0) {
    throw 'DNS servers unreachable from the PC must warn with record guidance, defer to the VNet check, and not prompt.'
}

$DnsRepaired = $false
$FailureMode = 'publicAddress'
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
Assert-PrivateEndpointDnsResolution
if ($PromptCount -ne 0 -or @($Warnings | Where-Object { $_ -like '*cannot be queried from this computer*' }).Count -ne 1) {
    throw 'Unattended installations from outside the VNet must continue and let the code deployment verify DNS.'
}
$FailureMode = 'publicAddress'
$Warnings.Clear()
Assert-DnsFailure -Expected 'Private endpoint DNS validation failed*'
if ($PromptCount -ne 0 -or
    @($Warnings | Where-Object { $_ -like '*Returned addresses: 52.0.0.1*' }).Count -ne 3) {
    throw 'Unattended runs must still fail without prompting when a reachable DNS server returns public addresses.'
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
