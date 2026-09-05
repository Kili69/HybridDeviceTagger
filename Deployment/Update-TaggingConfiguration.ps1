#Requires -Version 7.2
# Project-Version: 0.1.20260905.14
# Author: andreas.lucas@microsoft.com (aka Kili)

<#
DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
#>

[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingWriteHost',
    '',
    Justification = 'The operational script uses host output for readable previews and completion status.'
)]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSReviewUnusedParameter',
    '',
    Justification = 'Top-level script parameters are consumed by the normalization helper and update workflow.'
)]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSUseProcessBlockForPipelineCommand',
    '',
    Justification = 'AddRule accepts exactly one pipeline object and must execute the Azure update only once.'
)]
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 320)]
    [string]$FunctionAppAdministratorAccount,

    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$SubscriptionId,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 90)]
    [string]$ResourceGroupName,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(2, 60)]
    [string]$FunctionAppName,

    [uri]$ApiBaseUri = 'https://func-hybriddevicetagger.azurewebsites.net/api/management',

    [ValidateNotNull()]
    [ValidateCount(1, 100)]
    [object[]]$TaggingRules,

    [switch]$ForceTaggingRulesReplacement,

    [switch]$AddRule,

    [Parameter(ValueFromPipeline)]
    [object]$InputObject,

    [ValidateLength(1, 128)]
    [string]$RuleName,

    [ValidateLength(1, 1024)]
    [string]$RuleDescription,

    [ValidateLength(1, 2048)]
    [string]$BaseDn,

    [ValidateNotNull()]
    [ValidateCount(1, 15)]
    [ValidateRange(1, 15)]
    [int[]]$ExtensionAttribute,

    [Alias('Value')]
    [ValidateNotNull()]
    [ValidateCount(1, 15)]
    [ValidateLength(1, 1024)]
    [string[]]$ExtensionAttributeValue,

    [ValidateNotNull()]
    [ValidateCount(0, 50)]
    [guid[]]$EntraGroupId,

    [ValidateNotNull()]
    [ValidateCount(0, 50)]
    [guid[]]$AdministrativeUnitId,

    [ValidateNotNullOrEmpty()]
    [ValidateLength(1, 128)]
    [string]$RemoveRuleName,

    [switch]$ShowCurrent
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$script:RuleNamePattern = '^[\p{L}\p{N}][\p{L}\p{N} ._()\-]{0,127}$'
$script:MaximumRuleCount = 100
$script:MaximumRulesJsonLength = 32768
$script:MaximumRuleDescriptionLength = 1024
$script:MaximumBaseDnLength = 2048
$script:MaximumExtensionAttributeValueLength = 1024
$script:MaximumMembershipTargetCount = 50
$dnValuePattern = '(?!#)(?:\\[^\x00-\x1F\x7F]|[^,+"\\<>;\x00-\x1F\x7F])+'
$ouComponentPattern = "(?i:OU)\s*=$dnValuePattern"
$dcComponentPattern = "(?i:DC)\s*=$dnValuePattern"
$script:BaseDnPattern = "^\s*(?:$ouComponentPattern(?:\s*,\s*$ouComponentPattern)*(?:\s*,\s*$dcComponentPattern)*|$dcComponentPattern(?:\s*,\s*$dcComponentPattern)*)\s*$"

function Get-TaggingRuleValue {
    param(
        [Parameter(Mandatory)][object]$InputRule,
        [Parameter(Mandatory)][string]$PropertyName
    )

    if ($InputRule -is [System.Collections.IDictionary]) {
        return $InputRule[$PropertyName]
    }

    $property = $InputRule.PSObject.Properties[$PropertyName]
    if ($null -ne $property) {
        return $property.Value
    }
}

function ConvertTo-TaggingConfigurationOutput {
    param(
        [Parameter(Mandatory)][object[]]$Rules,
        [Parameter(Mandatory)][AllowEmptyString()][string]$RulesJson,
        [Parameter(Mandatory)][bool]$IsValid
    )

    [pscustomobject]@{
        PSTypeName = 'HybridDeviceTagger.TaggingConfiguration'
        FunctionAppName = $FunctionAppName
        ResourceGroupName = $ResourceGroupName
        Rules = [object[]]$Rules
        RulesJson = $RulesJson
        IsValid = $IsValid
    }
}

function Read-RequiredRuleValue {
    param(
        [Parameter(Mandatory)][string]$Prompt,
        [string]$Pattern,
        [string]$ValidationMessage,
        [ValidateRange(1, 32768)][int]$MaximumLength = 32768
    )

    while ($true) {
        try {
            $value = Read-Host -Prompt $Prompt
        }
        catch {
            throw 'Interactive rule input is unavailable. Supply InputObject or all required AddRule parameters.'
        }

        if ([string]::IsNullOrWhiteSpace($value)) {
            Write-Warning 'A non-empty value is required.'
            continue
        }
        if ($value.Length -gt $MaximumLength) {
            Write-Warning "Value cannot exceed $MaximumLength characters."
            continue
        }
        if (-not [string]::IsNullOrWhiteSpace($Pattern) -and $value -notmatch $Pattern) {
            Write-Warning $ValidationMessage
            continue
        }

        return $value
    }
}

function Read-MembershipTargetId {
    param([Parameter(Mandatory)][string]$TargetLabel)

    $ids = [System.Collections.Generic.List[guid]]::new()
    do {
        $rawId = Read-RequiredRuleValue `
            -Prompt "$TargetLabel Object ID" `
            -Pattern '^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$' `
            -ValidationMessage 'Enter a valid GUID Object ID.' `
            -MaximumLength 36
        $id = [guid]$rawId
        if ($ids -contains $id) {
            Write-Warning "$TargetLabel Object ID '$id' is already selected."
            continue
        }

        $ids.Add($id)
        if ($ids.Count -ge $script:MaximumMembershipTargetCount) {
            break
        }
        $addAnother = Read-Host "Add another $TargetLabel target? (y/N)"
    }
    while ($addAnother -match '^(?i:y|yes)$')

    $ids.ToArray()
}

function ConvertTo-NormalizedTaggingRule {
    param([Parameter(Mandatory)][object[]]$Rules)

    if ($Rules.Count -eq 0) {
        throw 'At least one tagging rule is required.'
    }
    if ($Rules.Count -gt $script:MaximumRuleCount) {
        throw "Tagging configuration cannot contain more than $script:MaximumRuleCount rules."
    }

    $normalizedRules = [System.Collections.Generic.List[object]]::new()
    $ruleNames = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase
    )
    foreach ($rule in $Rules) {
        $name = [string](Get-TaggingRuleValue -InputRule $rule -PropertyName 'Name')
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw 'Every tagging rule requires a non-empty Name. Replace legacy rules with -TaggingRules before using add or remove operations.'
        }
        if ($name.Trim() -notmatch $script:RuleNamePattern) {
            throw "Tagging rule name '$name' contains unsupported characters. Use 1-128 letters, numbers, spaces, dots, underscores, hyphens, or parentheses."
        }
        if (-not $ruleNames.Add($name.Trim())) {
            throw "Tagging rule name '$name' is duplicated."
        }
        $description = [string](Get-TaggingRuleValue -InputRule $rule -PropertyName 'Description')
        if ([string]::IsNullOrWhiteSpace($description)) {
            throw "Tagging rule '$name' requires a non-empty Description."
        }
        if ($description.Length -gt $script:MaximumRuleDescriptionLength) {
            throw "Tagging rule '$name' Description cannot exceed $script:MaximumRuleDescriptionLength characters."
        }
        $baseDn = [string]$rule.BaseDn
        if ([string]::IsNullOrWhiteSpace($baseDn)) {
            throw 'Every tagging rule requires a non-empty BaseDn.'
        }
        if ($baseDn.Length -gt $script:MaximumBaseDnLength) {
            throw "Tagging rule '$name' BaseDn cannot exceed $script:MaximumBaseDnLength characters."
        }
        if ($baseDn -notmatch $script:BaseDnPattern) {
            throw "Tagging rule '$baseDn' must be a valid OU distinguished name with an optional trailing DC part."
        }

        $rawAttributes = Get-TaggingRuleValue -InputRule $rule -PropertyName 'ExtensionAttributes'
        $attributes = [ordered]@{}
        $entries = if ($null -eq $rawAttributes) {
            @()
        }
        elseif ($rawAttributes -is [System.Collections.IDictionary]) {
            $rawAttributes.GetEnumerator() | ForEach-Object {
                [pscustomobject]@{ Name = [string]$_.Key; Value = [string]$_.Value }
            }
        }
        else {
            $rawAttributes.PSObject.Properties | ForEach-Object {
                [pscustomobject]@{ Name = $_.Name; Value = [string]$_.Value }
            }
        }

        foreach ($entry in @($entries | Sort-Object Name)) {
            if ($entry.Name -notmatch '^extensionAttribute(?:[1-9]|1[0-5])$') {
                throw "Tagging rule '$baseDn' contains unsupported attribute '$($entry.Name)'."
            }
            if ([string]::IsNullOrWhiteSpace($entry.Value)) {
                throw "Tagging rule '$baseDn' requires a non-empty value for '$($entry.Name)'."
            }
            if ($attributes.Contains($entry.Name)) {
                throw "Tagging rule '$baseDn' contains duplicate attribute '$($entry.Name)'."
            }
            $attributes[$entry.Name] = $entry.Value
        }
        if ($attributes.Count -gt 15) {
            throw "Tagging rule '$name' cannot contain more than 15 extension attributes."
        }

        foreach ($attribute in $attributes.GetEnumerator()) {
            if ($attribute.Value.Length -gt $script:MaximumExtensionAttributeValueLength) {
                throw "Tagging rule '$name' value for '$($attribute.Key)' cannot exceed $script:MaximumExtensionAttributeValueLength characters."
            }
        }

        $membershipTargets = [ordered]@{}
        foreach ($propertyName in @('EntraGroupIds', 'AdministrativeUnitIds')) {
            $targetIds = @(
                Get-TaggingRuleValue -InputRule $rule -PropertyName $propertyName |
                    Where-Object { $null -ne $_ }
            )
            if ($targetIds.Count -gt $script:MaximumMembershipTargetCount) {
                throw "Tagging rule '$name' cannot contain more than $script:MaximumMembershipTargetCount $propertyName."
            }

            $normalizedIds = [System.Collections.Generic.List[guid]]::new()
            $uniqueIds = [System.Collections.Generic.HashSet[guid]]::new()
            foreach ($targetId in $targetIds) {
                $parsedId = [guid]::Empty
                if (-not [guid]::TryParse([string]$targetId, [ref]$parsedId) -or
                    $parsedId -eq [guid]::Empty) {
                    throw "Tagging rule '$name' contains an invalid or empty GUID in $propertyName."
                }
                if (-not $uniqueIds.Add($parsedId)) {
                    throw "Tagging rule '$name' contains duplicate ID '$parsedId' in $propertyName."
                }
                $normalizedIds.Add($parsedId)
            }
            $membershipTargets[$propertyName] = $normalizedIds.ToArray()
        }
        if ($attributes.Count -eq 0 -and
            $membershipTargets.EntraGroupIds.Count -eq 0 -and
            $membershipTargets.AdministrativeUnitIds.Count -eq 0) {
            throw "Tagging rule '$name' requires at least one extension attribute, Entra group, or administrative unit."
        }

        $normalizedRules.Add([ordered]@{
            name = $name.Trim()
            description = $description.Trim()
            baseDn = $baseDn.Trim().TrimEnd(',')
            extensionAttributes = $attributes
            entraGroupIds = $membershipTargets.EntraGroupIds
            administrativeUnitIds = $membershipTargets.AdministrativeUnitIds
        })
    }

    $normalizedRules
}

$requestedOperations = @(
    $PSBoundParameters.ContainsKey('TaggingRules')
    $AddRule.IsPresent
    $PSBoundParameters.ContainsKey('RemoveRuleName')
).Where({ $_ }).Count
$addDetailParameters = @(
    'InputObject',
    'RuleName',
    'RuleDescription',
    'BaseDn',
    'ExtensionAttribute',
    'ExtensionAttributeValue',
    'EntraGroupId',
    'AdministrativeUnitId'
).Where({ $PSBoundParameters.ContainsKey($_) })
if (-not $AddRule -and $addDetailParameters.Count -gt 0) {
    throw "Parameters $($addDetailParameters -join ', ') can only be used with -AddRule."
}
if ($ForceTaggingRulesReplacement -and
    -not $PSBoundParameters.ContainsKey('TaggingRules')) {
    throw 'ForceTaggingRulesReplacement can only be used with -TaggingRules.'
}
if ($requestedOperations -eq 0 -and -not $ShowCurrent) {
    throw 'Specify -TaggingRules, -AddRule, or -RemoveRuleName to change the configuration; use -ShowCurrent to display it.'
}
if ($requestedOperations -gt 1) {
    throw 'Specify only one update operation: -TaggingRules, -AddRule, or -RemoveRuleName.'
}
if ($PSBoundParameters.ContainsKey('RemoveRuleName') -and
    $RemoveRuleName.Trim() -notmatch $script:RuleNamePattern) {
    throw "Tagging rule name '$RemoveRuleName' contains unsupported characters."
}

$prevalidatedRules = if ($PSBoundParameters.ContainsKey('TaggingRules')) {
    @(ConvertTo-NormalizedTaggingRule -Rules $TaggingRules)
}
else {
    @()
}
$addRuleUsesScalarParameters = $AddRule -and -not $PSBoundParameters.ContainsKey('InputObject')
$replaceEntraGroupIds = $PSBoundParameters.ContainsKey('EntraGroupId')
$replaceAdministrativeUnitIds = $PSBoundParameters.ContainsKey('AdministrativeUnitId')
$preparedNewRule = if ($AddRule) {
    $newRule = if ($PSBoundParameters.ContainsKey('InputObject')) {
        $scalarParameters = @(
            'RuleName',
            'RuleDescription',
            'BaseDn',
            'ExtensionAttribute',
            'ExtensionAttributeValue',
            'EntraGroupId',
            'AdministrativeUnitId'
        ).Where({ $PSBoundParameters.ContainsKey($_) })
        if ($scalarParameters.Count -gt 0) {
            throw "InputObject cannot be combined with scalar rule parameters: $($scalarParameters -join ', ')."
        }

        if ($InputObject -is [string]) {
            if ($InputObject.Length -gt $script:MaximumRulesJsonLength) {
                throw "InputObject JSON cannot exceed $script:MaximumRulesJsonLength characters."
            }
            try {
                $InputObject | ConvertFrom-Json
            }
            catch {
                throw "InputObject is not valid rule JSON: $($_.Exception.Message)"
            }
        }
        else {
            $InputObject
        }
    }
    else {
        $attributeNumbers = @(if ($PSBoundParameters.ContainsKey('ExtensionAttribute')) {
            $ExtensionAttribute
        })
        $attributeValues = @(if ($PSBoundParameters.ContainsKey('ExtensionAttributeValue')) {
            $ExtensionAttributeValue
        })
        if ($attributeNumbers.Count -gt 0 -and
            $attributeValues.Count -gt 0 -and
            $attributeNumbers.Count -ne $attributeValues.Count) {
            throw 'ExtensionAttribute and ExtensionAttributeValue must contain the same number of entries.'
        }
        if (@($attributeNumbers | Select-Object -Unique).Count -ne $attributeNumbers.Count) {
            throw 'ExtensionAttribute cannot contain duplicate numbers.'
        }

        $shouldPromptForAttributes = $attributeNumbers.Count -gt 0 -or $attributeValues.Count -gt 0

        $missingParameters = @(
            if ([string]::IsNullOrWhiteSpace($RuleName)) { 'RuleName' }
            if ($attributeNumbers.Count -gt 0 -and $attributeValues.Count -eq 0) { 'ExtensionAttributeValue' }
            if ($attributeValues.Count -gt 0 -and $attributeNumbers.Count -eq 0) { 'ExtensionAttribute' }
            if ($attributeNumbers.Count -eq 0 -and
                -not $PSBoundParameters.ContainsKey('EntraGroupId') -and
                -not $PSBoundParameters.ContainsKey('AdministrativeUnitId')) {
                'ExtensionAttribute or membership target'
            }
        )
        if ($missingParameters.Count -gt 0) {
            if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
                throw "AddRule without InputObject requires: $($missingParameters -join ', ')."
            }

            Write-Host 'Enter the missing tagging-rule values.' -ForegroundColor Cyan
            if ([string]::IsNullOrWhiteSpace($RuleName)) {
                $RuleName = Read-RequiredRuleValue `
                    -Prompt 'Rule name' `
                    -Pattern $script:RuleNamePattern `
                    -ValidationMessage 'Use 1-128 letters, numbers, spaces, dots, underscores, hyphens, or parentheses.' `
                    -MaximumLength 128
            }
            if ($attributeNumbers.Count -eq 0 -and
                $attributeValues.Count -eq 0 -and
                -not $replaceEntraGroupIds -and
                -not $replaceAdministrativeUnitIds) {
                $actions = Read-RequiredRuleValue `
                    -Prompt 'Actions: A=extension attribute, G=Entra group, U=administrative unit (combine letters)' `
                    -Pattern '^(?i:[AGU]{1,3})$' `
                    -ValidationMessage 'Enter A, G, U, or a combination such as AGU.' `
                    -MaximumLength 3
                $shouldPromptForAttributes = $actions -match '(?i:A)'
                if ($actions -match '(?i:G)') {
                    $EntraGroupId = @(Read-MembershipTargetId -TargetLabel 'Entra group')
                    $replaceEntraGroupIds = $true
                }
                if ($actions -match '(?i:U)') {
                    $AdministrativeUnitId = @(Read-MembershipTargetId -TargetLabel 'administrative unit')
                    $replaceAdministrativeUnitIds = $true
                }
            }
            if ($shouldPromptForAttributes -and $attributeNumbers.Count -eq 0 -and $attributeValues.Count -eq 0) {
                do {
                    do {
                        $attributeNumber = [int](Read-RequiredRuleValue `
                            -Prompt 'Extension attribute number (1-15)' `
                            -Pattern '^(?:[1-9]|1[0-5])$' `
                            -ValidationMessage 'Enter a number from 1 through 15.' `
                            -MaximumLength 2)
                        if ($attributeNumbers -contains $attributeNumber) {
                            Write-Warning "Extension attribute $attributeNumber is already selected."
                        }
                    }
                    while ($attributeNumbers -contains $attributeNumber)

                    $attributeNumbers += $attributeNumber
                    $attributeValues += Read-RequiredRuleValue `
                        -Prompt "Value for extensionAttribute$attributeNumber" `
                        -MaximumLength $script:MaximumExtensionAttributeValueLength
                    if ($attributeNumbers.Count -ge 15) {
                        break
                    }
                    $addAnotherAttribute = Read-Host 'Add another extension attribute? (y/N)'
                }
                while ($addAnotherAttribute -match '^(?i:y|yes)$')
            }
            elseif ($shouldPromptForAttributes -and $attributeValues.Count -eq 0) {
                foreach ($attributeNumber in $attributeNumbers) {
                    $attributeValues += Read-RequiredRuleValue `
                        -Prompt "Value for extensionAttribute$attributeNumber" `
                        -MaximumLength $script:MaximumExtensionAttributeValueLength
                }
            }
            elseif ($shouldPromptForAttributes -and $attributeNumbers.Count -eq 0) {
                foreach ($attributeValue in $attributeValues) {
                    do {
                        $attributeNumber = [int](Read-RequiredRuleValue `
                            -Prompt "Extension attribute number (1-15) for value '$attributeValue'" `
                            -Pattern '^(?:[1-9]|1[0-5])$' `
                            -ValidationMessage 'Enter a number from 1 through 15.' `
                            -MaximumLength 2)
                        if ($attributeNumbers -contains $attributeNumber) {
                            Write-Warning "Extension attribute $attributeNumber is already selected."
                        }
                    }
                    while ($attributeNumbers -contains $attributeNumber)
                    $attributeNumbers += $attributeNumber
                }
            }
        }

        $extensionAttributes = [ordered]@{}
        for ($attributeIndex = 0; $attributeIndex -lt $attributeNumbers.Count; $attributeIndex++) {
            $extensionAttributes["extensionAttribute$($attributeNumbers[$attributeIndex])"] =
                $attributeValues[$attributeIndex]
        }

        [ordered]@{
            Name = $RuleName
            Description = if ([string]::IsNullOrWhiteSpace($RuleDescription)) {
                "Rule $RuleName"
            } else {
                $RuleDescription
            }
            BaseDn = $BaseDn
            ExtensionAttributes = $extensionAttributes
            EntraGroupIds = @($EntraGroupId)
            AdministrativeUnitIds = @($AdministrativeUnitId)
        }
    }

    if ($newRule -is [System.Array]) {
        throw 'InputObject must contain exactly one tagging rule, not an array.'
    }

    if ($addRuleUsesScalarParameters) {
        $newRule
    }
    else {
        @(ConvertTo-NormalizedTaggingRule -Rules @($newRule))[0]
    }
}
else {
    $null
}

$prevalidatedRulesJson = ConvertTo-Json -InputObject $prevalidatedRules -Depth 10 -Compress
if ($prevalidatedRulesJson.Length -gt $script:MaximumRulesJsonLength) {
    throw "Serialized tagging configuration cannot exceed $script:MaximumRulesJsonLength characters."
}

Import-Module `
    (Join-Path $PSScriptRoot 'HybridDeviceTagger.Management/HybridDeviceTagger.Management.psd1') `
    -Force `
    -ErrorAction Stop
Connect-HybridDeviceTagger -ApiBaseUri $ApiBaseUri
try {
    $currentRules = @(Get-HybridDeviceTaggerRule)
}
catch {
    throw "Unable to read tagging rules through the management API: $($_.Exception.Message)"
}

$currentRulesJson = ConvertTo-Json -InputObject $currentRules -Depth 10 -Compress
$currentRulesParseError = $null

$currentConfiguration = ConvertTo-TaggingConfigurationOutput `
    -Rules $currentRules `
    -RulesJson $currentRulesJson `
    -IsValid ($null -eq $currentRulesParseError)
$displayCurrent = $ShowCurrent -or
    $AddRule -or
    $PSBoundParameters.ContainsKey('RemoveRuleName')
if ($displayCurrent) {
    if ([string]::IsNullOrWhiteSpace($currentRulesJson)) {
        Write-Host 'No current tagging rules are configured.'
    }
    elseif ($null -ne $currentRulesParseError) {
        Write-Warning "The current management API rule configuration is invalid JSON: $currentRulesParseError"
        Write-Host $currentRulesJson
    }
    else {
        Write-Host 'Current tagging rules:'
        Write-Host ($currentRulesJson | ConvertFrom-Json | ConvertTo-Json -Depth 10)
    }
}

if ($requestedOperations -eq 0) {
    Write-Output $currentConfiguration
    return
}
if ($null -ne $currentRulesParseError -and
    -not $PSBoundParameters.ContainsKey('TaggingRules')) {
    throw 'The current rule configuration is invalid JSON. Repair it by supplying the complete desired configuration with -TaggingRules.'
}

if ($addRuleUsesScalarParameters) {
    $matchingRules = @($currentRules | Where-Object {
        (Get-TaggingRuleValue -InputRule $_ -PropertyName 'Name') -ieq $RuleName
    })
    if ($matchingRules.Count -gt 1) {
        throw "Current configuration contains multiple rules named '$RuleName'."
    }

    if ($matchingRules.Count -eq 1) {
        $existingRule = @(ConvertTo-NormalizedTaggingRule -Rules $matchingRules)[0]
        $existingBaseDn = [string](Get-TaggingRuleValue -InputRule $existingRule -PropertyName 'BaseDn')
        $existingDescription = [string](Get-TaggingRuleValue -InputRule $existingRule -PropertyName 'Description')
        $existingAttributes = Get-TaggingRuleValue -InputRule $existingRule -PropertyName 'ExtensionAttributes'
        $existingGroupIds = Get-TaggingRuleValue -InputRule $existingRule -PropertyName 'EntraGroupIds'
        $existingAdministrativeUnitIds = Get-TaggingRuleValue -InputRule $existingRule -PropertyName 'AdministrativeUnitIds'
        $newAttributes = Get-TaggingRuleValue -InputRule $preparedNewRule -PropertyName 'ExtensionAttributes'
        $mergedAttributes = [ordered]@{}
        foreach ($attribute in $existingAttributes.GetEnumerator()) {
            $mergedAttributes[$attribute.Key] = $attribute.Value
        }
        foreach ($attribute in $newAttributes.GetEnumerator()) {
            $mergedAttributes[$attribute.Key] = $attribute.Value
        }

        $preparedNewRule = [ordered]@{
            Name = $RuleName
            Description = if ([string]::IsNullOrWhiteSpace($RuleDescription)) {
                $existingDescription
            } else {
                $RuleDescription
            }
            BaseDn = if ([string]::IsNullOrWhiteSpace($BaseDn)) {
                $existingBaseDn
            } else {
                $BaseDn
            }
            ExtensionAttributes = $mergedAttributes
            EntraGroupIds = if ($replaceEntraGroupIds) {
                @($EntraGroupId)
            } else {
                @($existingGroupIds)
            }
            AdministrativeUnitIds = if ($replaceAdministrativeUnitIds) {
                @($AdministrativeUnitId)
            } else {
                @($existingAdministrativeUnitIds)
            }
        }
    }
    else {
        if ([string]::IsNullOrWhiteSpace($BaseDn)) {
            if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
                throw 'BaseDn is required when AddRule creates a new rule.'
            }

            $BaseDn = Read-RequiredRuleValue `
                -Prompt 'Base DN' `
                -Pattern $script:BaseDnPattern `
                -ValidationMessage 'Enter a valid OU distinguished name, optionally followed by DC components.' `
                -MaximumLength $script:MaximumBaseDnLength
        }
        $preparedNewRule.BaseDn = $BaseDn
    }

    $preparedNewRule = @(ConvertTo-NormalizedTaggingRule -Rules @($preparedNewRule))[0]
}

$proposedRules = if ($PSBoundParameters.ContainsKey('TaggingRules')) {
    $prevalidatedRules
}
elseif ($AddRule) {
    if ($addRuleUsesScalarParameters -and $matchingRules.Count -eq 1) {
        @($currentRules | ForEach-Object {
            if ((Get-TaggingRuleValue -InputRule $_ -PropertyName 'Name') -ieq $RuleName) {
                $preparedNewRule
            }
            else {
                $_
            }
        })
    }
    else {
        @($currentRules) + @($preparedNewRule)
    }
}
else {
    $matchingRules = @($currentRules | Where-Object {
        (Get-TaggingRuleValue -InputRule $_ -PropertyName 'Name') -ieq $RemoveRuleName
    })
    if ($matchingRules.Count -eq 0) {
        throw "Tagging rule '$RemoveRuleName' was not found."
    }
    @($currentRules | Where-Object {
        (Get-TaggingRuleValue -InputRule $_ -PropertyName 'Name') -ine $RemoveRuleName
    })
}

$normalizedRules = if ($PSBoundParameters.ContainsKey('TaggingRules')) {
    $prevalidatedRules
}
else {
    @(ConvertTo-NormalizedTaggingRule -Rules $proposedRules)
}
$newRulesJson = ConvertTo-Json -InputObject $normalizedRules -Depth 10 -Compress
if ($newRulesJson.Length -gt $script:MaximumRulesJsonLength) {
    throw "Serialized tagging configuration cannot exceed $script:MaximumRulesJsonLength characters."
}
$currentCanonicalJson = if ([string]::IsNullOrWhiteSpace($currentRulesJson) -or
    $null -ne $currentRulesParseError) {
    ''
}
else {
    ConvertTo-Json -InputObject $currentRules -Depth 10 -Compress
}

Write-Host 'Proposed tagging rules:'
Write-Host (ConvertTo-Json -InputObject $normalizedRules -Depth 10)

if ($currentCanonicalJson -ceq $newRulesJson) {
    Write-Host 'The tagging configuration is already up to date.' -ForegroundColor Green
    Write-Output $currentConfiguration
    return
}

if ($PSBoundParameters.ContainsKey('TaggingRules') -and
    -not $WhatIfPreference -and
    -not $ForceTaggingRulesReplacement) {
    if ($env:TF_BUILD -eq 'True' -or $env:CI -eq 'true') {
        throw 'Replacing all tagging rules requires explicit approval. Re-run with -ForceTaggingRulesReplacement in an approved noninteractive pipeline.'
    }

    $confirmed = $PSCmdlet.ShouldContinue(
        'This replaces the complete tagging-rule configuration. Rules omitted from TaggingRules will be removed. Continue?',
        'Confirm complete tagging-rule replacement')
    if (-not $confirmed) {
        Write-Host 'Complete tagging-rule replacement canceled.' -ForegroundColor Yellow
        Write-Output $currentConfiguration
        return
    }
}

if (-not $PSCmdlet.ShouldProcess(
    $ApiBaseUri.AbsoluteUri,
    'Replace tagging rules through the Entra-authenticated management API')) {
    Write-Output $currentConfiguration
    return
}

try {
    $updatedConfiguration = Set-HybridDeviceTaggerRuleConfiguration `
        -Rule $normalizedRules `
        -Confirm:$false
}
catch {
    throw "Updating tagging rules through the management API failed: $($_.Exception.Message)"
}

Write-Host "Tagging configuration updated for Function App '$FunctionAppName'." -ForegroundColor Green
if (-not [string]::IsNullOrWhiteSpace($currentRulesJson)) {
    Write-Host 'Previous tagging-rule JSON (for rollback):'
    Write-Host $currentRulesJson
}

Write-Output (ConvertTo-TaggingConfigurationOutput `
    -Rules @($updatedConfiguration.rules) `
    -RulesJson (ConvertTo-Json -InputObject @($updatedConfiguration.rules) -Depth 10 -Compress) `
    -IsValid $true)