#Requires -Version 7.2

<#
.SYNOPSIS
Tests installer authorization without signing in or changing Azure resources.

.EXAMPLE
pwsh -NoProfile -File tests\InstallerAuthorization.Tests.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent

function Read-ScriptAst {
    param([string]$RelativePath)

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $repositoryRoot $RelativePath), [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) {
        throw ($parseErrors | Out-String)
    }
    $ast
}

$guidedAst = Read-ScriptAst 'installationspackage\Install.ps1'
$engineAst = Read-ScriptAst 'installationspackage\Install-HybridDeviceTagger.ps1'

foreach ($name in 'Get-SavedValue', 'Get-Answer', 'Get-NameSuffix',
    'Read-InstallationSetting', 'ConvertTo-EngineParameter') {
    $function = $guidedAst.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq $name
    }, $true)
    . ([scriptblock]::Create($function.Extent.Text))
}

function Write-Section {}
function Get-LoggedOnForest { 'contoso.com' }
function Read-YesNo { $true }
function Read-Value {
    param($Prompt, $Default, $Pattern, $ValidationMessage, $Optional, $Help)
    if ($Prompt -like '*Object ID of the user or group*') {
        throw 'The guided installer must not prompt for bootstrap principals.'
    }
    if ($Prompt -like '*root CA certificate*') {
        if ($Optional) { throw 'The root CA certificate must not be optional.' }
        $script:CertificatePromptCount++
        return $script:CertificateInput
    }
    if ($Default) { return $Default }
    if ($Optional) { return '' }
    'test-value'
}

$guidPattern = '^[0-9a-fA-F-]{36}$'
$dnsNamePattern = '.+'
$ncrontabPattern = '.+'
$AzureAccount = @{ id = '11111111-1111-1111-1111-111111111111' }
$ActiveDirectoryCredential = [pscredential]::new(
    'service@contoso.com', (ConvertTo-SecureString 'test-password' -AsPlainText -Force))
$CertificateInput = $PSCommandPath

foreach ($UseSavedSettings in $false, $true) {
    foreach ($certificatePath in $null, '', $PSCommandPath) {
    $CertificatePromptCount = 0
    $saved = @{
        ManagementApiAllowedPrincipalObjectId = '22222222-2222-2222-2222-222222222222'
        CreateKeyVault = $true
        ActiveDirectoryTrustedCertificatePath = $certificatePath
    }
    $settings = Read-InstallationSetting
    $expectedPrompts = if ($UseSavedSettings -and $certificatePath) { 0 } else { 1 }
    if ($CertificatePromptCount -ne $expectedPrompts -or
        $settings.ActiveDirectoryTrustedCertificatePath -ne $PSCommandPath) {
        throw 'Missing or empty saved root CA paths must prompt; valid saved paths must be preserved.'
    }
    if ($settings.Contains('ManagementApiAllowedPrincipalObjectId')) {
        throw 'Guided settings must not retain legacy bootstrap principals.'
    }
    $parameters = ConvertTo-EngineParameter -Settings $settings
    if ($parameters.ContainsKey('ManagementApiAllowedPrincipalObjectId')) {
        throw 'The guided installer must not supply bootstrap principals to the engine.'
    }
    $settings.ManagementApiAllowedPrincipalObjectId = $saved.ManagementApiAllowedPrincipalObjectId
    $parameters = ConvertTo-EngineParameter -Settings $settings
    if ($parameters.ContainsKey('ManagementApiAllowedPrincipalObjectId')) {
        throw 'Parameter conversion must ignore legacy bootstrap settings.'
    }
    if ($parameters.ADServiceAccountCredential -ne $ActiveDirectoryCredential -or
        $parameters.ContainsKey('ServiceAccountUpn')) {
        throw 'Parameter conversion must preserve the credential-only parameter set.'
    }
    if ($parameters.ActiveDirectoryTrustedCertificatePath -ne $PSCommandPath) {
        throw 'Parameter conversion must pass the required root CA path to the engine.'
    }
    }
}

$certificateFixture = Join-Path ([IO.Path]::GetTempPath()) "HybridDeviceTagger root $([guid]::NewGuid()).cer"
$null = New-Item -ItemType File -Path $certificateFixture
try {
    foreach ($UseSavedSettings in $false, $true) {
        foreach ($quote in '', '"', "'") {
            $CertificateInput = "  $quote$certificateFixture$quote  "
            $saved = @{
                ActiveDirectoryTrustedCertificatePath = $CertificateInput
                CreateKeyVault = $true
            }
            $settings = Read-InstallationSetting
            if ($settings.ActiveDirectoryTrustedCertificatePath -cne $certificateFixture) {
                throw 'Quoted and unquoted certificate paths containing spaces must resolve for new and saved answers.'
            }
            $parameters = ConvertTo-EngineParameter -Settings $settings
            if ($parameters.ActiveDirectoryTrustedCertificatePath -cne $certificateFixture) {
                throw 'The engine must receive the resolved certificate path without surrounding quotes.'
            }
        }
    }
}
finally {
    Remove-Item -LiteralPath $certificateFixture -Force
}

$roleAssignment = $engineAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
    $node.Left.Extent.Text -eq '$script:ManagementApiRoleAssignments'
}, $true)
$assignmentLoop = $engineAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.ForEachStatementAst] -and
    $node.Variable.VariablePath.UserPath -eq 'principalId' -and
    $node.Condition.Extent.Text -like '*ManagementApiRoleAssignments*'
}, $true)
$collectAssignments = [scriptblock]::Create(
    "foreach (`$roleDefinition in `$roleDefinitions) { foreach (`$principalId in $($assignmentLoop.Condition.Extent.Text)) { `$principalId } }")
$roleDefinitions = @(
    @{ value = 'HybridDeviceTagger.Reader' }
    @{ value = 'HybridDeviceTagger.RuleAdministrator' }
    @{ value = 'HybridDeviceTagger.TaggingOperator' }
    @{ value = 'HybridDeviceTagger.PasswordRotationAdministrator' }
)
$ManagementApiAllowedPrincipalObjectId = $null
$ManagementApiReaderPrincipalObjectId = $null
$ManagementApiRuleAdministratorPrincipalObjectId = $null
$ManagementApiTaggingOperatorPrincipalObjectId = $null
$ManagementApiPasswordRotationAdministratorPrincipalObjectId = $null

. ([scriptblock]::Create($roleAssignment.Extent.Text))
if (@(& $collectAssignments).Count -ne 0) {
    throw 'Omitting principal lists must not attempt any application-role assignment.'
}

$ManagementApiAllowedPrincipalObjectId = @([guid]::NewGuid(), [guid]::NewGuid())
$ManagementApiRuleAdministratorPrincipalObjectId = @([guid]::NewGuid())
. ([scriptblock]::Create($roleAssignment.Extent.Text))
$assigned = @(& $collectAssignments)
if ($assigned.Count -ne 7 -or
    @($assigned | Where-Object { $_ -eq $ManagementApiRuleAdministratorPrincipalObjectId[0] }).Count -ne 1 -or
    @($assigned | Where-Object { $_ -eq $ManagementApiAllowedPrincipalObjectId[0] }).Count -ne 3 -or
    @($assigned | Where-Object { $_ -eq $ManagementApiAllowedPrincipalObjectId[1] }).Count -ne 3) {
    throw 'Explicit bootstrap principals and role-specific overrides must remain supported.'
}

foreach ($name in 'Get-ManagementApiRoleDefinition', 'Set-ManagementApiAuthorization', 'Grant-GraphApplicationPermission') {
    $function = $engineAst.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq $name
    }, $true)
    . ([scriptblock]::Create($function.Extent.Text))
}

$ManagementApiClientId = '11111111-1111-1111-1111-111111111111'
$roleDefinitions = @(Get-ManagementApiRoleDefinition)
$ManagementApiRoleAssignments = [ordered]@{}
foreach ($role in $roleDefinitions) { $ManagementApiRoleAssignments[$role.value] = $null }
$MissingObject = ''
$GraphRoles = @()
$AzureWrites = [Collections.Generic.List[object]]::new()

function Get-AzureJson {
    param([string[]]$Arguments)
    $uri = $Arguments[[Array]::IndexOf($Arguments, '--url') + 1]
    if ($uri -like '*00000003-0000-0000-c000-000000000000*') {
        $objects = if ($script:MissingObject -eq 'graph') { @() } else {
            @([pscustomobject]@{ id = 'graph'; appRoles = $script:GraphRoles })
        }
        return [pscustomobject]@{ value = $objects }
    }
    if ($uri -like '*/applications?*') {
        $objects = if ($script:MissingObject -eq 'application') { @() } else { @($script:TestApplication) }
        return [pscustomobject]@{ value = $objects }
    }
    if ($uri -like '*/servicePrincipals?*') {
        $objects = if ($script:MissingObject -eq 'servicePrincipal') { @() } else { @($script:TestServicePrincipal) }
        return [pscustomobject]@{ value = $objects }
    }
    if ($uri -match '/appRoleAssign(ments|edTo)') {
        return [pscustomobject]@{ value = @() }
    }
    throw "Unexpected test Graph read: $uri"
}

function ConvertTo-AzureCliJsonArgument {
    param([string]$Json)
    $Json
}

function Invoke-AzureCli {
    param([string[]]$Arguments)
    $method = $Arguments[[Array]::IndexOf($Arguments, '--method') + 1]
    $uri = $Arguments[[Array]::IndexOf($Arguments, '--url') + 1]
    $body = $Arguments[[Array]::IndexOf($Arguments, '--body') + 1] | ConvertFrom-Json
    $script:AzureWrites.Add([pscustomobject]@{ Method = $method; Uri = $uri; Body = $body })
    if ($method -ne 'patch') { throw 'No bootstrap role assignment should be made.' }
    if ($null -ne $body.PSObject.Properties['api']) {
        foreach ($property in $body.api.PSObject.Properties) {
            $script:TestApplication.api.($property.Name) = $property.Value
        }
    }
    if ($null -ne $body.PSObject.Properties['appRoles']) {
        $script:TestApplication.appRoles = $body.appRoles
    }
    if ($null -ne $body.PSObject.Properties['appRoleAssignmentRequired']) {
        $script:TestServicePrincipal.appRoleAssignmentRequired = $body.appRoleAssignmentRequired
    }
}

function Assert-ExpectedError {
    param([scriptblock]$Action, [string]$ExpectedMessage)
    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -like $ExpectedMessage) { return }
        throw
    }
    throw "Expected an explicit error matching '$ExpectedMessage'."
}

$TestApplication = [pscustomobject]@{
    id = 'application'
    appId = $ManagementApiClientId
    appRoles = $roleDefinitions
    identifierUris = @("api://$ManagementApiClientId")
    api = [pscustomobject]@{ oauth2PermissionScopes = @(); preAuthorizedApplications = @() }
}
$TestServicePrincipal = [pscustomobject]@{ id = 'enterprise'; appRoleAssignmentRequired = $true }
Set-ManagementApiAuthorization
if ($TestApplication.api.oauth2PermissionScopes.Count -ne 1 -or
    $TestApplication.api.oauth2PermissionScopes[0].value -ne 'access_as_user' -or
    $TestApplication.api.preAuthorizedApplications.Count -ne 1 -or
    $TestApplication.api.preAuthorizedApplications[0].appId -ne '04b07795-8ddb-461a-bbee-02f9e1bf7b46' -or
    $TestServicePrincipal.appRoleAssignmentRequired -or $AzureWrites.Count -ne 3) {
    throw 'A new registration must receive the delegated scope and CLI preauthorization without bootstrap assignments.'
}

$AzureWrites.Clear()
Set-ManagementApiAuthorization
if ($AzureWrites.Count -ne 0) { throw 'An unchanged authorization configuration must not be rewritten.' }

$TestApplication.appRoles = @([pscustomobject]@{ id = 'unrelated-role'; value = 'CustomRole' })
$TestApplication.api.oauth2PermissionScopes = @([pscustomobject]@{ id = 'unrelated-scope'; value = 'custom_scope' })
$TestApplication.api.preAuthorizedApplications = @([pscustomobject]@{ appId = 'unrelated-client'; delegatedPermissionIds = @('unrelated-scope') })
Set-ManagementApiAuthorization
if ($TestApplication.appRoles.Count -ne 5 -or
    $TestApplication.appRoles[0].id -ne 'unrelated-role' -or
    $TestApplication.api.oauth2PermissionScopes.Count -ne 2 -or
    $TestApplication.api.oauth2PermissionScopes[0].id -ne 'unrelated-scope' -or
    $TestApplication.api.preAuthorizedApplications.Count -ne 2 -or
    $TestApplication.api.preAuthorizedApplications[0].appId -ne 'unrelated-client') {
    throw 'Missing entries must be created without dropping unrelated roles, scopes, or clients.'
}
$AzureWrites.Clear()
Set-ManagementApiAuthorization
if ($AzureWrites.Count -ne 0) { throw 'Migrated authorization settings must remain idempotent.' }

foreach ($MissingObject in 'application', 'servicePrincipal') {
    $expected = if ($MissingObject -eq 'application') {
        "*Management API application*was not found*"
    } else {
        "*Management API enterprise application*was not found*"
    }
    Assert-ExpectedError -Action { Set-ManagementApiAuthorization } -ExpectedMessage $expected
}
$MissingObject = 'graph'
Assert-ExpectedError -Action { Grant-GraphApplicationPermission -PrincipalId 'function' } `
    -ExpectedMessage 'Microsoft Graph service principal was not found*'
$MissingObject = ''
Assert-ExpectedError -Action { Grant-GraphApplicationPermission -PrincipalId 'function' } `
    -ExpectedMessage 'Microsoft Graph application role Device.ReadWrite.All was not found*'

$function = $engineAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Set-ConfigurationPortalSignIn'
}, $true)
. ([scriptblock]::Create($function.Extent.Text))
$FunctionAppName = 'func-test'
$PortalApplication = [pscustomobject]@{ id = 'application'; spa = [pscustomobject]@{ redirectUris = @('https://other.example/') } }
$PortalGrants = @()
$ConsentFails = $false
$PortalWrites = [Collections.Generic.List[object]]::new()
$PortalWarnings = [Collections.Generic.List[string]]::new()
function Write-Warning { param([string]$Message) $script:PortalWarnings.Add($Message) }
function Get-AzureJson {
    param([string[]]$Arguments)
    $uri = $Arguments[[Array]::IndexOf($Arguments, '--url') + 1]
    if ($uri -like '*/oauth2PermissionGrants') { return [pscustomobject]@{ value = @($script:PortalGrants) } }
    if ($uri -like '*/applications`?*') { return [pscustomobject]@{ value = @($script:PortalApplication) } }
    if ($uri -like '*/servicePrincipals`?*') { return [pscustomobject]@{ value = @([pscustomobject]@{ id = 'enterprise' }) } }
    throw "Unexpected portal Graph read: $uri"
}
function Invoke-AzureCli {
    param([string[]]$Arguments, [switch]$AllowFailure)
    $method = $Arguments[[Array]::IndexOf($Arguments, '--method') + 1]
    $uri = $Arguments[[Array]::IndexOf($Arguments, '--url') + 1]
    $body = $Arguments[[Array]::IndexOf($Arguments, '--body') + 1] | ConvertFrom-Json
    $script:PortalWrites.Add([pscustomobject]@{ Method = $method; Uri = $uri; Body = $body })
    if ($uri -like '*/applications/*') { $script:PortalApplication.spa = $body.spa }
    elseif ($script:ConsentFails) { return [pscustomobject]@{ ExitCode = 1; Output = 'Insufficient privileges' } }
    else { $script:PortalGrants = @([pscustomobject]@{ id = 'grant'; resourceId = 'enterprise'; consentType = 'AllPrincipals'; scope = $body.scope }) }
    [pscustomobject]@{ ExitCode = 0; Output = '' }
}

$portalUrl = Set-ConfigurationPortalSignIn
$redirectUris = @($PortalApplication.spa.redirectUris)
$grantWrite = $PortalWrites | Where-Object Uri -like '*/oauth2PermissionGrants*'
if ($portalUrl -ne 'https://func-test.azurewebsites.net/api/portal' -or
    $redirectUris.Count -ne 2 -or $redirectUris[0] -ne 'https://other.example/' -or $redirectUris[1] -ne $portalUrl -or
    $grantWrite.Method -ne 'post' -or $grantWrite.Body.consentType -ne 'AllPrincipals' -or
    $grantWrite.Body.clientId -ne 'enterprise' -or $grantWrite.Body.resourceId -ne 'enterprise' -or
    $grantWrite.Body.scope -ne 'access_as_user') {
    throw 'The portal redirect URI and tenant-wide self-consent must be added without dropping unrelated redirect URIs.'
}
$PortalWrites.Clear()
$null = Set-ConfigurationPortalSignIn
if ($PortalWrites.Count -ne 0) { throw 'Configured portal sign-in must remain idempotent.' }

$PortalGrants = @([pscustomobject]@{ id = 'grant'; resourceId = 'enterprise'; consentType = 'AllPrincipals'; scope = 'other_scope' })
$ConsentFails = $true
$null = Set-ConfigurationPortalSignIn
if ($PortalWrites.Count -ne 1 -or $PortalWrites[0].Method -ne 'patch' -or
    $PortalWrites[0].Body.scope -ne 'other_scope access_as_user' -or
    $PortalWarnings.Count -ne 1 -or $PortalWarnings[0] -notlike '*admin consent*') {
    throw 'Missing consent must extend existing scopes, and a failed grant must only warn.'
}

Write-Host 'Installer authorization tests passed.'
