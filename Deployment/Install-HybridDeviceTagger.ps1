#Requires -Version 7.2

[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingWriteHost',
    '',
    Justification = 'The installer uses colored host output for interactive progress and completion status.'
)]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSReviewUnusedParameter',
    '',
    Justification = 'Top-level script parameters are consumed by installer helper functions.'
)]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingPlainTextForPassword',
    '',
    Justification = 'These string parameters contain Key Vault entry names, never credential values.'
)]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSUseShouldProcessForStateChangingFunctions',
    '',
    Justification = 'Internal AD mutation helpers are guarded by an explicit default-No account confirmation and skipped for ValidateOnly and WhatIf.'
)]
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ExpectedAzureAccount,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F-]{36}$')]
    [string]$SubscriptionId,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ResourceGroupName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Location,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-zA-Z0-9-]{2,60}$')]
    [string]$FunctionAppName,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9]{3,24}$')]
    [string]$StorageAccountName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$VirtualNetworkName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$SubnetName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PrivateEndpointSubnetName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$VirtualNetworkResourceGroupName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$KeyVaultName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$KeyVaultResourceGroupName,

    [Parameter(Mandatory)]
    [ValidatePattern('^(?=.{1,253}$)([a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$')]
    [string]$ActiveDirectoryForestDomain,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ActiveDirectoryServer,

    [switch]$AllowLdapFallback,

    [ValidateRange(1, 65535)]
    [int]$LdapFallbackPort = 389,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ActiveDirectoryBindUsername,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ActiveDirectoryBindUserDistinguishedName,

    [ValidateNotNull()]
    [pscredential]$ActiveDirectoryCredential,

    [ValidateNotNull()]
    [pscredential]$ActiveDirectoryProvisioningCredential,

    [ValidatePattern('^[0-9a-fA-F-]{36}$')]
    [string]$VirtualNetworkSubscriptionId = $SubscriptionId,

    [ValidatePattern('^[0-9a-fA-F-]{36}$')]
    [string]$KeyVaultSubscriptionId = $SubscriptionId,

    [ValidateNotNullOrEmpty()]
    [string]$CurrentCredentialEntryName = 'hybrid-device-tagger-ad-password-current',

    [ValidateNotNullOrEmpty()]
    [string]$StagedCredentialEntryName = 'hybrid-device-tagger-ad-password-next',

    [Parameter(Mandatory)]
    [ValidateNotNull()]
    [object[]]$TaggingRules,

    [ValidateNotNullOrEmpty()]
    [string]$TaggingSchedule = '0 */15 * * * *',

    [ValidateNotNullOrEmpty()]
    [string]$RotationSchedule = '0 30 2 * * *',

    [ValidateRange(1, 1000)]
    [int]$MaximumInstanceCount = 10,

    [ValidateSet(512, 2048, 4096)]
    [int]$InstanceMemoryMB = 2048,

    [ValidateRange(4, 256)]
    [int]$InitialPasswordLength = 40,

    [ValidateRange(26, 365)]
    [int]$InitialSecretExpiryDays = 35,

    [hashtable]$Tags = @{},

    [switch]$CreateResourceGroup,
    [switch]$RegisterProviders,
    [switch]$PrepareActiveDirectoryAccountOnly,
    [switch]$ValidateOnly,
    [switch]$SkipCodeDeployment
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptRoot = $PSScriptRoot
$repositoryRoot = Split-Path -Parent $scriptRoot
$templatePath = Join-Path $scriptRoot 'main.bicep'
$projectPath = Join-Path $repositoryRoot 'src/HybridDeviceTagger/HybridDeviceTagger.csproj'
$requiredProviders = @(
    'Microsoft.App',
    'Microsoft.Authorization',
    'Microsoft.Insights',
    'Microsoft.KeyVault',
    'Microsoft.Network',
    'Microsoft.OperationalInsights',
    'Microsoft.Storage',
    'Microsoft.Web'
)

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Invoke-AzureCli {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = & az @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "Azure CLI failed (az $($Arguments -join ' ')):`n$text"
    }

    [pscustomobject]@{
        ExitCode = $exitCode
        Output = $text
    }
}

function Get-AzureJson {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $result = Invoke-AzureCli -Arguments ($Arguments + @('--only-show-errors', '--output', 'json'))
    if ([string]::IsNullOrWhiteSpace($result.Output)) {
        return $null
    }
    $result.Output | ConvertFrom-Json -Depth 100
}

function ConvertTo-AzureCliJsonArgument {
    param([Parameter(Mandatory)][string]$Json)

    if ($IsWindows) {
        return $Json.Replace('"', '\"')
    }

    $Json
}

function Assert-Command {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$InstallHint
    )
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found. $InstallHint"
    }
}

function Convert-DnsNameToDistinguishedName {
    param([Parameter(Mandatory)][string]$DnsName)
    ($DnsName.Trim('.').Split('.') | ForEach-Object { "DC=$_" }) -join ','
}

function Test-ForestRelativeOuPath {
    param([Parameter(Mandatory)][string]$Path)
    $Path -notmatch '(?i)(?:^|(?<!\\),)\s*DC\s*='
}

function Test-OuOnlyPath {
    param([Parameter(Mandatory)][string]$Path)
    @($Path.Trim().TrimEnd(',') -split '(?<!\\),').Where({ $_ -notmatch '(?i)^\s*OU\s*=' }).Count -eq 0
}

function Get-DomainDnsNameFromDistinguishedName {
    param([Parameter(Mandatory)][string]$DistinguishedName)
    @([regex]::Matches(
        $DistinguishedName,
        '(?i)(?:^|(?<!\\),)\s*DC\s*=\s*([^,]+)'
    ) | ForEach-Object { $_.Groups[1].Value.Trim() }) -join '.'
}

function ConvertTo-NormalizedTaggingRule {
    if ($TaggingRules.Count -eq 0) {
        throw 'At least one tagging rule is required.'
    }

    $normalizedRules = [System.Collections.Generic.List[object]]::new()
    foreach ($rule in $TaggingRules) {
        $baseDn = [string]$rule.BaseDn
        if ([string]::IsNullOrWhiteSpace($baseDn)) {
            throw 'Every tagging rule requires a non-empty BaseDn.'
        }
        if ((Test-ForestRelativeOuPath $baseDn) -and -not (Test-OuOnlyPath $baseDn)) {
            throw "Tagging rule '$baseDn' must be a full distinguished name or an OU-only path."
        }

        $attributes = [System.Collections.Generic.Dictionary[string, string]]::new(
            [StringComparer]::OrdinalIgnoreCase
        )
        $rawAttributes = $rule.ExtensionAttributes
        if ($null -eq $rawAttributes) {
            throw "Tagging rule '$baseDn' requires ExtensionAttributes."
        }

        $entries = if ($rawAttributes -is [System.Collections.IDictionary]) {
            $rawAttributes.GetEnumerator() | ForEach-Object {
                [pscustomobject]@{ Name = [string]$_.Key; Value = [string]$_.Value }
            }
        }
        else {
            $rawAttributes.PSObject.Properties | ForEach-Object {
                [pscustomobject]@{ Name = $_.Name; Value = [string]$_.Value }
            }
        }

        foreach ($entry in @($entries)) {
            if ($entry.Name -notmatch '^extensionAttribute(?:[1-9]|1[0-5])$') {
                throw "Tagging rule '$baseDn' contains unsupported attribute '$($entry.Name)'."
            }
            if ([string]::IsNullOrWhiteSpace($entry.Value)) {
                throw "Tagging rule '$baseDn' requires a non-empty value for '$($entry.Name)'."
            }
            if (-not $attributes.TryAdd($entry.Name, $entry.Value)) {
                throw "Tagging rule '$baseDn' contains duplicate attribute '$($entry.Name)'."
            }
        }
        if ($attributes.Count -eq 0) {
            throw "Tagging rule '$baseDn' requires at least one extension attribute."
        }

        $normalizedRules.Add([ordered]@{
            baseDn = $baseDn
            extensionAttributes = $attributes
        })
    }

    $normalizedRules
}

function Connect-LdapDirectory {
    param(
        [Parameter(Mandatory)][string]$Server,
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][bool]$UseSsl,
        [Parameter()][pscredential]$Credential = $ActiveDirectoryCredential
    )

    $identifier = [System.DirectoryServices.Protocols.LdapDirectoryIdentifier]::new(
        $Server,
        $Port,
        $false,
        $false
    )
    $authenticationType = if ($UseSsl) {
        [System.DirectoryServices.Protocols.AuthType]::Basic
    }
    else {
        [System.DirectoryServices.Protocols.AuthType]::Negotiate
    }
    $connection = [System.DirectoryServices.Protocols.LdapConnection]::new(
        $identifier,
        $Credential.GetNetworkCredential(),
        $authenticationType
    )
    $connection.Timeout = [TimeSpan]::FromSeconds(15)
    $connection.SessionOptions.ProtocolVersion = 3
    $connection.SessionOptions.SecureSocketLayer = $UseSsl
    if (-not $UseSsl) {
        $connection.SessionOptions.Signing = $true
        $connection.SessionOptions.Sealing = $true
    }

    try {
        $connection.Bind()
        $connection
    }
    catch {
        $connection.Dispose()
        throw
    }
}

function Confirm-LdapFallbackUse {
    if ($script:EffectiveAllowLdapFallback) {
        return $true
    }

    $warning = @"
LDAPS is unavailable. Continuing with LDAP on port $LdapFallbackPort uses Negotiate authentication with LDAP signing and sealing.
The connection does not use TLS, but the negotiated SASL security layer protects the bind and LDAP traffic.
Password rotation will remain disabled until LDAPS becomes available.
"@
    Write-Warning $warning

    if ($AllowLdapFallback.IsPresent) {
        Write-Warning 'LDAP fallback was explicitly approved through -AllowLdapFallback.'
        return $true
    }

    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'Interactive confirmation is unavailable in CI. Re-run with -AllowLdapFallback to explicitly approve unencrypted LDAP.'
    }

    $choices = @(
        [System.Management.Automation.Host.ChoiceDescription]::new('&Yes', 'Use unencrypted LDAP for read and bind operations.'),
        [System.Management.Automation.Host.ChoiceDescription]::new('&No', 'Cancel without enabling LDAP fallback.')
    )
    try {
        $choice = $Host.UI.PromptForChoice(
            'Approve unencrypted LDAP fallback?',
            'Do you want to continue with LDAP instead of LDAPS?',
            $choices,
            1
        )
    }
    catch {
        throw 'LDAP fallback requires interactive confirmation. Re-run with -AllowLdapFallback for noninteractive execution.'
    }

    if ($choice -ne 0) {
        throw 'Installation canceled because LDAPS is unavailable and LDAP fallback was not approved.'
    }

    $true
}

function Connect-ValidatedLdapDirectory {
    param([Parameter(Mandatory)][string]$Server)

    try {
        Connect-LdapDirectory -Server $Server -Port 636 -UseSsl $true
    }
    catch {
        if (-not (Test-LdapConnectionUnavailable -Exception $_.Exception)) {
            throw
        }

        $null = Confirm-LdapFallbackUse
        $connection = Connect-LdapDirectory `
            -Server $Server `
            -Port $LdapFallbackPort `
            -UseSsl $false
        $script:EffectiveAllowLdapFallback = $true
        $connection
    }
}

function Confirm-ActiveDirectoryAccountChange {
    param([Parameter(Mandatory)][ValidateSet('Create', 'Reset')][string]$Action)

    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw "Active Directory account $($Action.ToLowerInvariant()) requires interactive confirmation."
    }

    $verb = if ($Action -eq 'Create') { 'create' } else { 'reset the password for' }
    $choices = @(
        [System.Management.Automation.Host.ChoiceDescription]::new('&Yes', "$Action the configured service account."),
        [System.Management.Automation.Host.ChoiceDescription]::new('&No', 'Cancel without changing Active Directory.')
    )
    $choice = $Host.UI.PromptForChoice(
        "$Action Active Directory service account?",
        "Do you want to $verb '$ActiveDirectoryBindUserDistinguishedName'?",
        $choices,
        1
    )
    if ($choice -ne 0) {
        throw 'Installation canceled without changing the Active Directory service account.'
    }
}

function Get-ActiveDirectoryAccountName {
    $accountName = if ($ActiveDirectoryBindUsername.Contains('@')) {
        $ActiveDirectoryBindUsername.Split('@', 2)[0]
    }
    elseif ($ActiveDirectoryBindUsername.Contains('\')) {
        $ActiveDirectoryBindUsername.Split('\', 2)[1]
    }
    else {
        $ActiveDirectoryBindUsername
    }
    if ($accountName.Length -gt 20) {
        throw "The sAMAccountName derived from '$ActiveDirectoryBindUsername' exceeds 20 characters."
    }
    $accountName
}

function New-ComplexRandomPassword {
    param([Parameter(Mandatory)][ValidateRange(4, 256)][int]$Length)

    $characterSets = @(
        'ABCDEFGHJKLMNPQRSTUVWXYZ',
        'abcdefghijkmnopqrstuvwxyz',
        '23456789',
        '!#$%&*+-=?@_'
    )
    $allCharacters = $characterSets -join ''
    $characters = [char[]]::new($Length)

    for ($index = 0; $index -lt $characterSets.Count; $index++) {
        $set = $characterSets[$index]
        $characters[$index] = $set[[Security.Cryptography.RandomNumberGenerator]::GetInt32($set.Length)]
    }
    for ($index = $characterSets.Count; $index -lt $characters.Length; $index++) {
        $characters[$index] = $allCharacters[
            [Security.Cryptography.RandomNumberGenerator]::GetInt32($allCharacters.Length)
        ]
    }
    for ($index = $characters.Length - 1; $index -gt 0; $index--) {
        $swapIndex = [Security.Cryptography.RandomNumberGenerator]::GetInt32($index + 1)
        $temporary = $characters[$index]
        $characters[$index] = $characters[$swapIndex]
        $characters[$swapIndex] = $temporary
    }

    -join $characters
}

function Set-RandomActiveDirectoryCredential {
    $plainPassword = New-ComplexRandomPassword -Length $InitialPasswordLength
    try {
        $securePassword = [Security.SecureString]::new()
        foreach ($character in $plainPassword.ToCharArray()) {
            $securePassword.AppendChar($character)
        }
        $securePassword.MakeReadOnly()
        $script:ActiveDirectoryCredential = [pscredential]::new(
            $ActiveDirectoryBindUsername,
            $securePassword
        )
    }
    finally {
        $plainPassword = $null
    }
}

function Select-ActiveDirectoryResetCredential {
    $choices = @(
        [System.Management.Automation.Host.ChoiceDescription]::new(
            '&Randomized',
            'Reset to a cryptographically random complex password.'
        ),
        [System.Management.Automation.Host.ChoiceDescription]::new(
            '&Enter',
            'Enter the new password securely.'
        ),
        [System.Management.Automation.Host.ChoiceDescription]::new(
            '&Cancel',
            'Cancel without changing Active Directory.'
        )
    )
    $choice = $Host.UI.PromptForChoice(
        'Reset Active Directory service-account password?',
        "Choose the new password source for '$ActiveDirectoryBindUserDistinguishedName'.",
        $choices,
        2
    )

    switch ($choice) {
        0 { Set-RandomActiveDirectoryCredential }
        1 {
            $script:ActiveDirectoryCredential = Get-Credential `
                -UserName $ActiveDirectoryBindUsername `
                -Message 'Enter the new Active Directory service-account password'
            if ($null -eq $script:ActiveDirectoryCredential) {
                throw 'Password entry was canceled without changing Active Directory.'
            }
        }
        default { throw 'Installation canceled without changing the Active Directory service account.' }
    }
}

function Set-ActiveDirectoryAccountPassword {
    param(
        [Parameter(Mandatory)][System.DirectoryServices.Protocols.LdapConnection]$Connection,
        [Parameter(Mandatory)][bool]$EnableAccount
    )

    $quotedPassword = '"' + $ActiveDirectoryCredential.GetNetworkCredential().Password + '"'
    $passwordBytes = [Text.Encoding]::Unicode.GetBytes($quotedPassword)
    try {
        $passwordChange = [System.DirectoryServices.Protocols.DirectoryAttributeModification]::new()
        $passwordChange.Name = 'unicodePwd'
        $passwordChange.Operation = [System.DirectoryServices.Protocols.DirectoryAttributeOperation]::Replace
        $null = $passwordChange.Add($passwordBytes)
        $passwordRequest = [System.DirectoryServices.Protocols.ModifyRequest]::new(
            $ActiveDirectoryBindUserDistinguishedName,
            $passwordChange
        )
        $null = $Connection.SendRequest($passwordRequest)

        $attributesRequest = [System.DirectoryServices.Protocols.ModifyRequest]::new(
            $ActiveDirectoryBindUserDistinguishedName
        )
        $pwdLastSet = [System.DirectoryServices.Protocols.DirectoryAttributeModification]::new()
        $pwdLastSet.Name = 'pwdLastSet'
        $pwdLastSet.Operation = [System.DirectoryServices.Protocols.DirectoryAttributeOperation]::Replace
        $null = $pwdLastSet.Add('-1')
        $null = $attributesRequest.Modifications.Add($pwdLastSet)
        if ($EnableAccount) {
            $userAccountControl = [System.DirectoryServices.Protocols.DirectoryAttributeModification]::new()
            $userAccountControl.Name = 'userAccountControl'
            $userAccountControl.Operation = [System.DirectoryServices.Protocols.DirectoryAttributeOperation]::Replace
            $null = $userAccountControl.Add('512')
            $null = $attributesRequest.Modifications.Add($userAccountControl)
        }
        $null = $Connection.SendRequest($attributesRequest)
    }
    finally {
        [Array]::Clear($passwordBytes, 0, $passwordBytes.Length)
    }
}

function Test-LdapConnectionUnavailable {
    param([Parameter(Mandatory)][Exception]$Exception)

    $currentException = $Exception
    while ($null -ne $currentException) {
        if ($currentException -is [System.DirectoryServices.Protocols.LdapException]) {
            return $currentException.ErrorCode -in 51, 52, 81, 85, 91
        }
        $currentException = $currentException.InnerException
    }

    $false
}

function Initialize-ActiveDirectoryServiceAccountWithAdModule {
    Import-Module ActiveDirectory -ErrorAction Stop

    try {
        $account = Get-ADUser `
            -Identity $ActiveDirectoryBindUserDistinguishedName `
            -Server $ActiveDirectoryServer `
            -Credential $script:ActiveDirectoryProvisioningCredential `
            -ErrorAction Stop
    }
    catch {
        if ($_.Exception.GetType().FullName -ne 'Microsoft.ActiveDirectory.Management.ADIdentityNotFoundException') {
            throw
        }
        $account = $null
    }

    if ($null -ne $account) {
        Select-ActiveDirectoryResetCredential
        Set-ADAccountPassword `
            -Identity $ActiveDirectoryBindUserDistinguishedName `
            -Server $ActiveDirectoryServer `
            -Credential $script:ActiveDirectoryProvisioningCredential `
            -Reset `
            -NewPassword $script:ActiveDirectoryCredential.Password
        Set-ADUser `
            -Identity $ActiveDirectoryBindUserDistinguishedName `
            -Server $ActiveDirectoryServer `
            -Credential $script:ActiveDirectoryProvisioningCredential `
            -ChangePasswordAtLogon $false
        $script:ActiveDirectoryPasswordChanged = $true
        Write-Host 'The AD service-account password was reset through Active Directory Web Services; password change at next logon is disabled.' -ForegroundColor Green
        return
    }

    Confirm-ActiveDirectoryAccountChange -Action Create
    Set-RandomActiveDirectoryCredential
    $dnMatch = [regex]::Match($ActiveDirectoryBindUserDistinguishedName, '(?i)^CN=([^,]+),(.+)$')
    if (-not $dnMatch.Success) {
        throw 'ActiveDirectoryBindUserDistinguishedName must start with an unescaped CN followed by its parent DN.'
    }

    $parameters = @{
        Name = $dnMatch.Groups[1].Value
        SamAccountName = Get-ActiveDirectoryAccountName
        Path = $dnMatch.Groups[2].Value
        Server = $ActiveDirectoryServer
        Credential = $script:ActiveDirectoryProvisioningCredential
        AccountPassword = $script:ActiveDirectoryCredential.Password
        Enabled = $true
        ChangePasswordAtLogon = $false
    }
    if ($ActiveDirectoryBindUsername.Contains('@')) {
        $parameters.UserPrincipalName = $ActiveDirectoryBindUsername
    }
    New-ADUser @parameters
    $script:ActiveDirectoryPasswordChanged = $true
    Write-Host 'The AD service account was created through Active Directory Web Services with a randomized complex password and enabled; password change at next logon is disabled.' -ForegroundColor Green
}

function Initialize-ActiveDirectoryServiceAccount {
    Add-Type -AssemblyName System.DirectoryServices.Protocols

    if ($ValidateOnly -or $WhatIfPreference) {
        return
    }
    if ($null -eq $ActiveDirectoryProvisioningCredential) {
        $script:ActiveDirectoryProvisioningCredential = Get-Credential `
            -Message 'Credential permitted to create or reset the configured AD service account'
    }
    if ($null -eq $script:ActiveDirectoryProvisioningCredential) {
        throw 'An Active Directory provisioning credential is required for installation.'
    }

    $connection = $null
    try {
        try {
            $connection = Connect-LdapDirectory `
                -Server $ActiveDirectoryServer `
                -Port 636 `
                -UseSsl $true `
                -Credential $script:ActiveDirectoryProvisioningCredential
        }
        catch {
            if (-not (Test-LdapConnectionUnavailable -Exception $_.Exception)) {
                throw
            }
            $null = Confirm-LdapFallbackUse
            Write-Warning 'Using the Active Directory PowerShell module over Active Directory Web Services for account bootstrap because LDAPS is unavailable.'
            Initialize-ActiveDirectoryServiceAccountWithAdModule
            return
        }
        $searchRequest = [System.DirectoryServices.Protocols.SearchRequest]::new(
            $ActiveDirectoryBindUserDistinguishedName,
            '(objectClass=user)',
            [System.DirectoryServices.Protocols.SearchScope]::Base,
            [string[]]@('distinguishedName')
        )
        try {
            $response = [System.DirectoryServices.Protocols.SearchResponse]$connection.SendRequest($searchRequest)
            $accountExists = $response.Entries.Count -eq 1
        }
        catch [System.DirectoryServices.Protocols.DirectoryOperationException] {
            if ($_.Exception.Response.ResultCode -ne [System.DirectoryServices.Protocols.ResultCode]::NoSuchObject) {
                throw
            }
            $accountExists = $false
        }

        if ($accountExists) {
            Select-ActiveDirectoryResetCredential
            Set-ActiveDirectoryAccountPassword -Connection $connection -EnableAccount $false
            $script:ActiveDirectoryPasswordChanged = $true
            Write-Host 'The AD service-account password was reset securely; password change at next logon is disabled.' -ForegroundColor Green
            return
        }

        Confirm-ActiveDirectoryAccountChange -Action Create
        Set-RandomActiveDirectoryCredential
        $dnMatch = [regex]::Match($ActiveDirectoryBindUserDistinguishedName, '(?i)^CN=([^,]+),(.+)$')
        if (-not $dnMatch.Success) {
            throw 'ActiveDirectoryBindUserDistinguishedName must start with an unescaped CN followed by its parent DN.'
        }
        $addRequest = [System.DirectoryServices.Protocols.AddRequest]::new(
            $ActiveDirectoryBindUserDistinguishedName
        )
        $objectClass = [System.DirectoryServices.Protocols.DirectoryAttribute]::new('objectClass')
        foreach ($className in @('top', 'person', 'organizationalPerson', 'user')) {
            $null = $objectClass.Add($className)
        }
        $null = $addRequest.Attributes.Add($objectClass)
        $null = $addRequest.Attributes.Add(
            [System.DirectoryServices.Protocols.DirectoryAttribute]::new('cn', $dnMatch.Groups[1].Value)
        )
        $null = $addRequest.Attributes.Add(
            [System.DirectoryServices.Protocols.DirectoryAttribute]::new(
                'sAMAccountName',
                (Get-ActiveDirectoryAccountName)
            )
        )
        $null = $addRequest.Attributes.Add(
            [System.DirectoryServices.Protocols.DirectoryAttribute]::new('userAccountControl', '514')
        )
        if ($ActiveDirectoryBindUsername.Contains('@')) {
            $null = $addRequest.Attributes.Add(
                [System.DirectoryServices.Protocols.DirectoryAttribute]::new(
                    'userPrincipalName',
                    $ActiveDirectoryBindUsername
                )
            )
        }
        $null = $connection.SendRequest($addRequest)
        Set-ActiveDirectoryAccountPassword -Connection $connection -EnableAccount $true
        $script:ActiveDirectoryPasswordChanged = $true
        Write-Host 'The AD service account was created with a randomized complex password and enabled; password change at next logon is disabled.' -ForegroundColor Green
    }
    finally {
        if ($null -ne $connection) {
            $connection.Dispose()
        }
    }
}

function Test-LdapConfiguration {
    Add-Type -AssemblyName System.DirectoryServices.Protocols

    if ($ActiveDirectoryCredential.UserName -ine $ActiveDirectoryBindUsername) {
        throw "ActiveDirectoryCredential.UserName must match ActiveDirectoryBindUsername."
    }

    $connection = $null
    $script:EffectiveAllowLdapFallback = $false
    try {
        $connection = Connect-ValidatedLdapDirectory -Server $ActiveDirectoryServer
        $rootRequest = [System.DirectoryServices.Protocols.SearchRequest]::new(
            '',
            '(objectClass=*)',
            [System.DirectoryServices.Protocols.SearchScope]::Base,
            [string[]]@('configurationNamingContext', 'defaultNamingContext', 'rootDomainNamingContext', 'dnsHostName')
        )
        $rootResponse = [System.DirectoryServices.Protocols.SearchResponse]$connection.SendRequest($rootRequest)
        if ($rootResponse.Entries.Count -ne 1) {
            throw 'The LDAP RootDSE query did not return exactly one entry.'
        }

        $expectedForestDn = Convert-DnsNameToDistinguishedName $ActiveDirectoryForestDomain
        $rootDomainNamingContext = [string]$rootResponse.Entries[0].Attributes['rootDomainNamingContext'][0]
        if ($rootDomainNamingContext -ine $expectedForestDn) {
            throw "AD forest mismatch. Expected '$expectedForestDn', received '$rootDomainNamingContext'."
        }

        $configurationNamingContext = [string]$rootResponse.Entries[0].Attributes['configurationNamingContext'][0]
        $partitionsRequest = [System.DirectoryServices.Protocols.SearchRequest]::new(
            "CN=Partitions,$configurationNamingContext",
            '(&(objectClass=crossRef)(systemFlags:1.2.840.113556.1.4.803:=3))',
            [System.DirectoryServices.Protocols.SearchScope]::OneLevel,
            [string[]]@('nCName', 'dnsRoot')
        )
        $partitionsResponse = [System.DirectoryServices.Protocols.SearchResponse]$connection.SendRequest($partitionsRequest)
        $forestDomains = @($partitionsResponse.Entries | ForEach-Object {
            [pscustomobject]@{
                NamingContext = [string]$_.Attributes['nCName'][0]
                DnsRoot = [string]$_.Attributes['dnsRoot'][0]
            }
        } | Where-Object { $_.NamingContext -and $_.DnsRoot })
        if ($forestDomains.Count -eq 0) {
            throw 'No Active Directory domains were found in the forest configuration partition.'
        }
    }
    finally {
        if ($null -ne $connection) {
            $connection.Dispose()
        }
    }

    foreach ($configuredBaseDn in @($script:NormalizedTaggingRules.baseDn | Select-Object -Unique)) {
        $isForestRelative = Test-ForestRelativeOuPath $configuredBaseDn
        $searchTargets = if ($isForestRelative) {
            @($forestDomains | ForEach-Object {
                [pscustomobject]@{
                    Server = $_.DnsRoot
                    BaseDn = "$($configuredBaseDn.Trim().TrimEnd(',')),$($_.NamingContext)"
                }
            })
        }
        else {
            @([pscustomobject]@{
                Server = Get-DomainDnsNameFromDistinguishedName $configuredBaseDn
                BaseDn = $configuredBaseDn
            })
        }
        $resolvedOuCount = 0

        foreach ($target in $searchTargets) {
            $domainConnection = $null
            try {
                $domainConnection = Connect-ValidatedLdapDirectory -Server $target.Server
                $baseRequest = [System.DirectoryServices.Protocols.SearchRequest]::new(
                    $target.BaseDn,
                    '(objectClass=*)',
                    [System.DirectoryServices.Protocols.SearchScope]::Base,
                    [string[]]@('distinguishedName')
                )
                $baseResponse = [System.DirectoryServices.Protocols.SearchResponse]$domainConnection.SendRequest($baseRequest)
                if ($baseResponse.Entries.Count -ne 1) {
                    throw "Active Directory base DN '$($target.BaseDn)' was not found."
                }
                $resolvedOuCount++

                $deviceRequest = [System.DirectoryServices.Protocols.SearchRequest]::new(
                    $target.BaseDn,
                    '(objectCategory=computer)',
                    [System.DirectoryServices.Protocols.SearchScope]::Subtree,
                    [string[]]@('msDS-DeviceID')
                )
                $deviceRequest.SizeLimit = 1
                try {
                    $null = $domainConnection.SendRequest($deviceRequest)
                }
                catch [System.DirectoryServices.Protocols.DirectoryOperationException] {
                    if ($_.Exception.Response.ResultCode -ne [System.DirectoryServices.Protocols.ResultCode]::SizeLimitExceeded) {
                        throw
                    }
                }
            }
            catch [System.DirectoryServices.Protocols.DirectoryOperationException] {
                if (-not $isForestRelative -or
                    $_.Exception.Response.ResultCode -ne [System.DirectoryServices.Protocols.ResultCode]::NoSuchObject) {
                    throw
                }
                Write-Host "OU path '$configuredBaseDn' is absent in domain '$($target.Server)'; skipping that domain."
            }
            finally {
                if ($null -ne $domainConnection) {
                    $domainConnection.Dispose()
                }
            }
        }

        if ($isForestRelative -and $resolvedOuCount -eq 0) {
            throw "OU path '$configuredBaseDn' was not found in any domain in the forest."
        }
    }

    $accountServer = Get-DomainDnsNameFromDistinguishedName $ActiveDirectoryBindUserDistinguishedName
    $accountConnection = $null
    try {
        $accountConnection = Connect-ValidatedLdapDirectory -Server $accountServer
        $accountRequest = [System.DirectoryServices.Protocols.SearchRequest]::new(
            $ActiveDirectoryBindUserDistinguishedName,
            '(objectClass=user)',
            [System.DirectoryServices.Protocols.SearchScope]::Base,
            [string[]]@('distinguishedName', 'userAccountControl', 'pwdLastSet')
        )
        $accountResponse = [System.DirectoryServices.Protocols.SearchResponse]$accountConnection.SendRequest($accountRequest)
        if ($accountResponse.Entries.Count -ne 1) {
            throw "AD service account '$ActiveDirectoryBindUserDistinguishedName' was not found."
        }
    }
    finally {
        if ($null -ne $accountConnection) {
            $accountConnection.Dispose()
        }
    }

    if (-not $script:EffectiveAllowLdapFallback) {
        Write-Host 'LDAPS validation succeeded for all required domains. LDAP fallback will be disabled.' -ForegroundColor Green
    }
}

function Assert-Subnet {
    param([Parameter(Mandatory)]$Subnet)

    if ($SubnetName.Contains('_')) {
        throw "Subnet name '$SubnetName' contains an underscore, which FC1 does not support."
    }

    $prefixes = @()
    if ($null -ne $Subnet.PSObject.Properties['addressPrefix'] -and $Subnet.addressPrefix) {
        $prefixes += $Subnet.addressPrefix
    }
    if ($null -ne $Subnet.PSObject.Properties['addressPrefixes'] -and $Subnet.addressPrefixes) {
        $prefixes += @($Subnet.addressPrefixes)
    }
    $ipv4Prefixes = @($prefixes | Where-Object { $_ -match '/(?<length>\d+)$' -and $_ -notmatch ':' })
    if ($ipv4Prefixes.Count -eq 0) {
        throw 'The integration subnet must have an IPv4 address prefix.'
    }
    foreach ($prefix in $ipv4Prefixes) {
        $prefixLength = [int]($prefix -replace '^.*/', '')
        if ($prefixLength -gt 27) {
            throw "Subnet prefix '$prefix' is smaller than the FC1 minimum recommendation of /27."
        }
    }

    $delegations = @($Subnet.delegations | ForEach-Object { $_.serviceName })
    if ($delegations -notcontains 'Microsoft.App/environments') {
        throw "Subnet '$SubnetName' must be delegated to Microsoft.App/environments."
    }
    if (@($delegations | Where-Object { $_ -ne 'Microsoft.App/environments' }).Count -gt 0) {
        throw "Subnet '$SubnetName' has unsupported additional delegations: $($delegations -join ', ')."
    }
    if (@($Subnet.serviceEndpoints).Count -gt 0) {
        throw "FC1 integration subnet '$SubnetName' must not contain service endpoints."
    }
}

function Assert-KeyVault {
    param(
        [Parameter(Mandatory)]$Vault,
        [switch]$SkipCurrentCredential
    )

    if (-not $Vault.enableRbacAuthorization) {
        throw "Key Vault '$KeyVaultName' must use Azure RBAC authorization."
    }
    if (-not $Vault.enableSoftDelete) {
        throw "Key Vault '$KeyVaultName' must have soft delete enabled."
    }
    if (-not $Vault.enablePurgeProtection) {
        throw "Key Vault '$KeyVaultName' must have purge protection enabled."
    }
    if ($Vault.publicNetworkAccess -eq 'Disabled' -or $Vault.networkAcls.defaultAction -eq 'Deny') {
        $approvedConnections = @($Vault.privateEndpointConnections | Where-Object {
            $_.privateLinkServiceConnectionState.status -eq 'Approved'
        })
        if ($approvedConnections.Count -eq 0) {
            throw "Restricted Key Vault '$KeyVaultName' requires an approved private endpoint connection."
        }
    }

    if ($SkipCurrentCredential) {
        return
    }

    $secret = Invoke-AzureCli -Arguments @(
        'keyvault', 'secret', 'show',
        '--vault-name', $KeyVaultName,
        '--name', $CurrentCredentialEntryName,
        '--subscription', $KeyVaultSubscriptionId,
        '--query', '{enabled:attributes.enabled,expires:attributes.expires,id:id,value:value}',
        '--only-show-errors', '--output', 'json'
    ) -AllowFailure
    if ($secret.ExitCode -ne 0) {
        throw "Current AD password secret '$CurrentCredentialEntryName' is missing or inaccessible in Key Vault '$KeyVaultName'."
    }

    $secretMetadata = $secret.Output | ConvertFrom-Json
    if ($secretMetadata.enabled -eq $false) {
        throw "Current AD password secret '$CurrentCredentialEntryName' is disabled."
    }
    if ($secretMetadata.value -cne $ActiveDirectoryCredential.GetNetworkCredential().Password) {
        throw 'The current Key Vault credential does not match the AD credential supplied for LDAPS validation.'
    }
    if ($secretMetadata.expires) {
        $minimumExpiry = [DateTimeOffset]::UtcNow.AddDays(25)
        if ([DateTimeOffset]::Parse($secretMetadata.expires) -le $minimumExpiry) {
            throw "Current AD password secret expires before the 25-day rotation interval."
        }
    }

}

function Set-CurrentPasswordSecret {
    param([Parameter(Mandatory)][string]$VaultUri)

    $tokenResult = Invoke-AzureCli -Arguments @(
        'account', 'get-access-token', '--subscription', $KeyVaultSubscriptionId,
        '--resource', 'https://vault.azure.net', '--only-show-errors', '--output', 'json'
    )
    $accessToken = ($tokenResult.Output | ConvertFrom-Json).accessToken
    $plainPassword = $ActiveDirectoryCredential.GetNetworkCredential().Password
    try {
        $body = @{
            value = $plainPassword
            attributes = @{
                enabled = $true
                exp = [DateTimeOffset]::UtcNow.AddDays($InitialSecretExpiryDays).ToUnixTimeSeconds()
            }
        } | ConvertTo-Json -Compress
        $secretUri = '{0}/secrets/{1}?api-version=7.4' -f $VaultUri.TrimEnd('/'), $CurrentCredentialEntryName
        $null = Invoke-RestMethod `
            -Method Put `
            -Uri $secretUri `
            -Headers @{ Authorization = "Bearer $accessToken" } `
            -ContentType 'application/json' `
            -Body $body
    }
    finally {
        $plainPassword = $null
        $body = $null
        $accessToken = $null
    }

    Write-Host "The current AD password was stored in Key Vault '$KeyVaultName'." -ForegroundColor Green
}

function Assert-ExistingOwnedResource {
    $storage = Invoke-AzureCli -Arguments @(
        'storage', 'account', 'show', '--subscription', $SubscriptionId,
        '--resource-group', $ResourceGroupName, '--name', $StorageAccountName,
        '--only-show-errors', '--output', 'json'
    ) -AllowFailure
    if ($storage.ExitCode -eq 0) {
        $storageAccount = $storage.Output | ConvertFrom-Json
        if ($storageAccount.location -ine $Location -or $storageAccount.kind -ne 'StorageV2') {
            throw "Existing storage account '$StorageAccountName' is not a StorageV2 account in '$Location'."
        }
        if ($storageAccount.minimumTlsVersion -ne 'TLS1_2' -or $storageAccount.allowBlobPublicAccess -ne $false) {
            throw "Existing storage account '$StorageAccountName' must enforce TLS 1.2 and disable public blob access."
        }
    }
    else {
        $availability = Get-AzureJson -Arguments @(
            'storage', 'account', 'check-name', '--subscription', $SubscriptionId,
            '--name', $StorageAccountName
        )
        if (-not $availability.nameAvailable) {
            throw "Storage account name '$StorageAccountName' is unavailable: $($availability.message)"
        }
    }

    $app = Invoke-AzureCli -Arguments @(
        'functionapp', 'show', '--subscription', $SubscriptionId,
        '--resource-group', $ResourceGroupName, '--name', $FunctionAppName,
        '--only-show-errors', '--output', 'json'
    ) -AllowFailure
    if ($app.ExitCode -eq 0) {
        $functionApp = $app.Output | ConvertFrom-Json
        $functionAppLocation = ([string]$functionApp.location) -replace '[^a-zA-Z0-9]', ''
        if ($functionAppLocation -ine $Location -or $functionApp.kind -notmatch 'functionapp' -or $functionApp.kind -notmatch 'linux') {
            throw "Existing app '$FunctionAppName' is not a Linux Function App in '$Location'."
        }
        if ($functionApp.identity.type -notmatch 'SystemAssigned') {
            throw "Existing Function App '$FunctionAppName' must have a system-assigned identity."
        }
        $functionAppSubnetId = if ($null -ne $functionApp.PSObject.Properties['virtualNetworkSubnetId']) {
            [string]$functionApp.virtualNetworkSubnetId
        }
        else {
            ''
        }
        if ($functionAppSubnetId -and $functionAppSubnetId -ine $script:ExpectedSubnetId) {
            throw "Existing Function App '$FunctionAppName' is integrated with a different subnet."
        }
    }
    else {
        $availability = Get-AzureJson -Arguments @(
            'rest', '--method', 'post',
            '--url', "https://management.azure.com/subscriptions/$SubscriptionId/providers/Microsoft.Web/checkNameAvailability?api-version=2024-04-01",
            '--body', (ConvertTo-AzureCliJsonArgument -Json (
                @{ name = $FunctionAppName; type = 'Microsoft.Web/sites' } | ConvertTo-Json -Compress
            )),
            '--headers', 'Content-Type=application/json'
        )
        if (-not $availability.nameAvailable) {
            throw "Function App name '$FunctionAppName' is unavailable: $($availability.message)"
        }
    }
}

function Get-DeploymentParameter {
    $values = [ordered]@{
        location = $Location
        functionAppName = $FunctionAppName
        storageAccountName = $StorageAccountName
        virtualNetworkName = $VirtualNetworkName
        subnetName = $SubnetName
        privateEndpointSubnetName = $PrivateEndpointSubnetName
        virtualNetworkResourceGroupName = $VirtualNetworkResourceGroupName
        virtualNetworkSubscriptionId = $VirtualNetworkSubscriptionId
        keyVaultName = $KeyVaultName
        keyVaultResourceGroupName = $KeyVaultResourceGroupName
        keyVaultSubscriptionId = $KeyVaultSubscriptionId
        activeDirectoryServer = $ActiveDirectoryServer
        allowLdapFallback = $script:EffectiveAllowLdapFallback
        ldapFallbackPort = $LdapFallbackPort
        activeDirectoryBindUsername = $ActiveDirectoryBindUsername
        activeDirectoryBindUserDistinguishedName = $ActiveDirectoryBindUserDistinguishedName
        taggingRules = $script:NormalizedTaggingRules
        currentPasswordSecretName = $CurrentCredentialEntryName
        nextPasswordSecretName = $StagedCredentialEntryName
        taggingSchedule = $TaggingSchedule
        rotationSchedule = $RotationSchedule
        maximumInstanceCount = $MaximumInstanceCount
        instanceMemoryMB = $InstanceMemoryMB
        tags = $Tags
    }

    $parameters = [ordered]@{}
    foreach ($entry in $values.GetEnumerator()) {
        $parameters[$entry.Key] = @{ value = $entry.Value }
    }
    @{ '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'; contentVersion = '1.0.0.0'; parameters = $parameters }
}

function Grant-GraphDevicePermission {
    param([Parameter(Mandatory)][string]$PrincipalId)

    $graphServicePrincipal = Get-AzureJson -Arguments @(
        'rest', '--method', 'get',
        '--url', 'https://graph.microsoft.com/v1.0/servicePrincipals?$filter=appId%20eq%20%2700000003-0000-0000-c000-000000000000%27'
    )
    $graph = @($graphServicePrincipal.value)[0]
    if (-not $graph) {
        throw 'Microsoft Graph service principal was not found in the tenant.'
    }
    $appRole = @($graph.appRoles | Where-Object {
        $_.value -eq 'Device.ReadWrite.All' -and $_.allowedMemberTypes -contains 'Application'
    })[0]
    if (-not $appRole) {
        throw 'Microsoft Graph application role Device.ReadWrite.All was not found.'
    }

    $assignments = Get-AzureJson -Arguments @(
        'rest', '--method', 'get',
        '--url', "https://graph.microsoft.com/v1.0/servicePrincipals/$PrincipalId/appRoleAssignments"
    )
    $existing = @($assignments.value | Where-Object {
        $_.resourceId -eq $graph.id -and $_.appRoleId -eq $appRole.id
    })
    if ($existing.Count -gt 0) {
        Write-Host 'Microsoft Graph Device.ReadWrite.All is already assigned.'
        return
    }

    $body = @{
        principalId = $PrincipalId
        resourceId = $graph.id
        appRoleId = $appRole.id
    } | ConvertTo-Json -Compress
    $body = ConvertTo-AzureCliJsonArgument -Json $body
    $null = Invoke-AzureCli -Arguments @(
        'rest', '--method', 'post',
        '--url', "https://graph.microsoft.com/v1.0/servicePrincipals/$PrincipalId/appRoleAssignments",
        '--headers', 'Content-Type=application/json',
        '--body', $body,
        '--only-show-errors'
    )
}

function Publish-FunctionApp {
    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "HybridDeviceTagger-$([guid]::NewGuid())"
    $publishPath = Join-Path $temporaryRoot 'publish'
    $zipPath = Join-Path $temporaryRoot 'HybridDeviceTagger.zip'
    try {
        $null = New-Item -ItemType Directory -Path $publishPath -Force
        & dotnet publish $projectPath --configuration Release --output $publishPath
        if ($LASTEXITCODE -ne 0) {
            throw 'dotnet publish failed.'
        }
        Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $zipPath -CompressionLevel Optimal
        $null = Invoke-AzureCli -Arguments @(
            'functionapp', 'deployment', 'source', 'config-zip',
            '--subscription', $SubscriptionId,
            '--resource-group', $ResourceGroupName,
            '--name', $FunctionAppName,
            '--src', $zipPath,
            '--only-show-errors'
        )
    }
    finally {
        if (Test-Path $temporaryRoot) {
            Remove-Item -Path $temporaryRoot -Recurse -Force
        }
    }
}

Write-Step 'Checking local prerequisites'
Assert-Command -Name az -InstallHint 'Install Azure CLI 2.60.0 or newer.'
Assert-Command -Name dotnet -InstallHint 'Install the .NET 8 SDK.'
if (-not (Test-Path $templatePath)) { throw "Bicep template not found: $templatePath" }
if (-not (Test-Path $projectPath)) { throw "Function project not found: $projectPath" }
if (-not $PrepareActiveDirectoryAccountOnly -and $null -eq $ActiveDirectoryCredential) {
    throw 'ActiveDirectoryCredential is required unless PrepareActiveDirectoryAccountOnly is used.'
}
$script:NormalizedTaggingRules = @(ConvertTo-NormalizedTaggingRule)
$script:ActiveDirectoryPasswordChanged = $false
$script:EffectiveAllowLdapFallback = $false

$azVersion = Get-AzureJson -Arguments @('version')
if ([version]$azVersion.'azure-cli' -lt [version]'2.60.0') {
    throw "Azure CLI 2.60.0 or newer is required; installed version is $($azVersion.'azure-cli')."
}
$sdkVersions = & dotnet --list-sdks
if (-not ($sdkVersions -match '^8\.')) {
    throw 'A .NET 8 SDK is required.'
}

Write-Step 'Validating Azure account and subscription'
$account = Get-AzureJson -Arguments @('account', 'show')
if ($account.user.name -ine $ExpectedAzureAccount) {
    throw "Azure account mismatch. Expected '$ExpectedAzureAccount', active account is '$($account.user.name)'."
}
if ($account.id -ine $SubscriptionId) {
    $null = Invoke-AzureCli -Arguments @('account', 'set', '--subscription', $SubscriptionId, '--only-show-errors')
    $account = Get-AzureJson -Arguments @('account', 'show')
}
if ($account.id -ine $SubscriptionId) {
    throw "Unable to select subscription '$SubscriptionId'."
}

Write-Step 'Checking Azure resource providers'
foreach ($provider in $requiredProviders) {
    $state = (Invoke-AzureCli -Arguments @(
        'provider', 'show', '--namespace', $provider,
        '--subscription', $SubscriptionId,
        '--query', 'registrationState', '--output', 'tsv', '--only-show-errors'
    )).Output.Trim()
    if ($state -ne 'Registered') {
        if ($RegisterProviders -and -not $ValidateOnly -and -not $WhatIfPreference) {
            $null = Invoke-AzureCli -Arguments @(
                'provider', 'register', '--namespace', $provider,
                '--subscription', $SubscriptionId, '--wait', '--only-show-errors'
            )
        }
        else {
            throw "Provider '$provider' is '$state'. Re-run an installation with -RegisterProviders."
        }
    }
}

Write-Step 'Validating resource group and FC1 region'
$resourceGroupResult = Invoke-AzureCli -Arguments @(
    'group', 'show', '--subscription', $SubscriptionId,
    '--name', $ResourceGroupName, '--only-show-errors', '--output', 'json'
) -AllowFailure
if ($resourceGroupResult.ExitCode -ne 0) {
    if (-not $CreateResourceGroup) {
        throw "Resource group '$ResourceGroupName' does not exist. Use -CreateResourceGroup for installation."
    }
    if ($ValidateOnly -or $WhatIfPreference) {
        throw "Resource group '$ResourceGroupName' must exist before validation or What-if can run."
    }
    if ($PSCmdlet.ShouldProcess("$SubscriptionId/$ResourceGroupName", "Create resource group in $Location")) {
        $null = Invoke-AzureCli -Arguments @(
            'group', 'create', '--subscription', $SubscriptionId,
            '--name', $ResourceGroupName, '--location', $Location,
            '--only-show-errors'
        )
    }
}
$fc1Locations = Get-AzureJson -Arguments @('functionapp', 'list-flexconsumption-locations')
if (@($fc1Locations | Where-Object { $_.name -ieq $Location }).Count -eq 0) {
    throw "Location '$Location' does not support Azure Functions Flex Consumption."
}

Write-Step 'Validating existing virtual network and subnet'
$virtualNetwork = Get-AzureJson -Arguments @(
    'network', 'vnet', 'show', '--subscription', $VirtualNetworkSubscriptionId,
    '--resource-group', $VirtualNetworkResourceGroupName, '--name', $VirtualNetworkName
)
if ($virtualNetwork.location -ine $Location) {
    throw "VNet location '$($virtualNetwork.location)' must match Function App location '$Location'."
}
$subnet = Get-AzureJson -Arguments @(
    'network', 'vnet', 'subnet', 'show', '--subscription', $VirtualNetworkSubscriptionId,
    '--resource-group', $VirtualNetworkResourceGroupName, '--vnet-name', $VirtualNetworkName,
    '--name', $SubnetName
)
$script:ExpectedSubnetId = [string]$subnet.id
Assert-Subnet -Subnet $subnet

$privateEndpoints = Get-AzureJson -Arguments @(
    'network', 'private-endpoint', 'list', '--subscription', $VirtualNetworkSubscriptionId
)
if (@($privateEndpoints | Where-Object { $_.subnet.id -ieq $subnet.id }).Count -gt 0) {
    throw "FC1 integration subnet '$SubnetName' must not contain private endpoints."
}

$null = Get-AzureJson -Arguments @(
    'network', 'vnet', 'subnet', 'show', '--subscription', $VirtualNetworkSubscriptionId,
    '--resource-group', $VirtualNetworkResourceGroupName, '--vnet-name', $VirtualNetworkName,
    '--name', $PrivateEndpointSubnetName
)

Write-Step 'Validating existing Key Vault and password secret'
$vault = Get-AzureJson -Arguments @(
    'keyvault', 'show', '--subscription', $KeyVaultSubscriptionId,
    '--resource-group', $KeyVaultResourceGroupName, '--name', $KeyVaultName
)
$vaultConfiguration = if ($null -ne $vault.PSObject.Properties['properties']) {
    $vault.properties
}
else {
    $vault
}
Assert-KeyVault -Vault $vaultConfiguration -SkipCurrentCredential:$PrepareActiveDirectoryAccountOnly

Write-Step 'Validating existing app-owned resources and global names'
Assert-ExistingOwnedResource

if ($PrepareActiveDirectoryAccountOnly) {
    Write-Step 'Preparing Active Directory service account'
    Initialize-ActiveDirectoryServiceAccount
    if (-not $script:ActiveDirectoryPasswordChanged) {
        throw 'Active Directory account preparation did not set a password.'
    }
    Set-CurrentPasswordSecret -VaultUri $vaultConfiguration.vaultUri
    Assert-KeyVault -Vault $vaultConfiguration
}

Write-Step 'Validating Active Directory forest, OU, account, read access, and LDAPS certificate'
Test-LdapConfiguration

if ($PrepareActiveDirectoryAccountOnly) {
    Write-Host 'Active Directory service-account preparation completed. No Azure resources were deployed.' -ForegroundColor Green
    return
}

$parameterFile = Join-Path ([System.IO.Path]::GetTempPath()) "HybridDeviceTagger-$([guid]::NewGuid()).parameters.json"
try {
    $parameterJson = Get-DeploymentParameter | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText(
        $parameterFile,
        $parameterJson,
        [System.Text.UTF8Encoding]::new($false)
    )

    Write-Step 'Validating Bicep deployment'
    $null = Invoke-AzureCli -Arguments @(
        'deployment', 'group', 'validate', '--subscription', $SubscriptionId,
        '--resource-group', $ResourceGroupName, '--template-file', $templatePath,
        '--parameters', "@$parameterFile", '--only-show-errors'
    )

    if ($ValidateOnly) {
        Write-Host "Validation completed successfully. No resources were changed." -ForegroundColor Green
        return
    }

    if ($WhatIfPreference) {
        Write-Step 'Running Azure Resource Manager What-if'
        $whatIfResult = Invoke-AzureCli -Arguments @(
            'deployment', 'group', 'what-if', '--subscription', $SubscriptionId,
            '--resource-group', $ResourceGroupName, '--template-file', $templatePath,
            '--parameters', "@$parameterFile", '--result-format', 'FullResourcePayloads'
        )
        if (-not [string]::IsNullOrWhiteSpace($whatIfResult.Output)) {
            Write-Host $whatIfResult.Output
        }
        Write-Host 'What-if completed. No resources were changed.' -ForegroundColor Green
        return
    }

    if (-not $PSCmdlet.ShouldProcess("$SubscriptionId/$ResourceGroupName", 'Deploy HybridDeviceTagger infrastructure and permissions')) {
        return
    }

    Write-Step 'Deploying Azure infrastructure'
    $deploymentName = "hybrid-device-tagger-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"
    $deployment = Get-AzureJson -Arguments @(
        'deployment', 'group', 'create', '--subscription', $SubscriptionId,
        '--resource-group', $ResourceGroupName, '--name', $deploymentName,
        '--template-file', $templatePath, '--parameters', "@$parameterFile"
    )
    $principalId = [string]$deployment.properties.outputs.functionAppPrincipalId.value
    if ([string]::IsNullOrWhiteSpace($principalId)) {
        throw 'The deployment did not return the Function App managed identity principal ID.'
    }

    Write-Step 'Assigning Microsoft Graph Device.ReadWrite.All'
    Grant-GraphDevicePermission -PrincipalId $principalId

    if (-not $SkipCodeDeployment) {
        Write-Step 'Building and deploying Function App code'
        Publish-FunctionApp
    }

    Write-Host "Installation completed for Function App '$FunctionAppName'." -ForegroundColor Green
}
finally {
    if ([System.IO.File]::Exists($parameterFile)) {
        [System.IO.File]::Delete($parameterFile)
    }
}