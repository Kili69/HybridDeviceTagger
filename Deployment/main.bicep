targetScope = 'resourceGroup'

type taggingRule = {
  @description('Full domain-specific DN, or an OU-only path resolved in every forest domain.')
  baseDn: string

  @description('Map of extensionAttribute1 through extensionAttribute15 to desired values.')
  extensionAttributes: object
}

@description('Azure region for all newly created resources. Must match the integration subnet region.')
param location string = resourceGroup().location

@description('Globally unique Azure Function App name.')
param functionAppName string

@description('Globally unique storage account name. Use 3-24 lowercase letters and numbers.')
@minLength(3)
@maxLength(24)
param storageAccountName string

@description('Name of the existing virtual network.')
param virtualNetworkName string

@description('Name of the existing subnet delegated to Microsoft.App/environments.')
param subnetName string

@description('Name of the existing subnet used for the deployment storage private endpoint.')
param privateEndpointSubnetName string

@description('Resource group containing the existing virtual network.')
param virtualNetworkResourceGroupName string

@description('Subscription containing the existing virtual network.')
param virtualNetworkSubscriptionId string = subscription().subscriptionId

@description('Name of the existing Key Vault.')
param keyVaultName string

@description('Resource group containing the existing Key Vault.')
param keyVaultResourceGroupName string

@description('Subscription containing the existing Key Vault.')
param keyVaultSubscriptionId string = subscription().subscriptionId

@description('DNS name of any domain controller in the target Active Directory forest.')
param activeDirectoryServer string

@description('Allow read and bind operations to fall back to unencrypted LDAP when LDAPS is unavailable.')
param allowLdapFallback bool = false

@description('Port used for unencrypted LDAP fallback.')
@minValue(1)
@maxValue(65535)
param ldapFallbackPort int = 389

@description('UPN or DOMAIN\\user name used to bind to Active Directory.')
param activeDirectoryBindUsername string

@description('Distinguished name of the Active Directory service account whose password is rotated.')
param activeDirectoryBindUserDistinguishedName string

@description('OU subtree rules and the Entra extension attributes assigned to matching devices.')
@minLength(1)
param taggingRules taggingRule[]

@description('Current Active Directory password secret name.')
param currentPasswordSecretName string = 'hybrid-device-tagger-ad-password-current'

@description('Staged Active Directory password secret name.')
param nextPasswordSecretName string = 'hybrid-device-tagger-ad-password-next'

@description('Timer schedule for device tagging.')
param taggingSchedule string = '0 */15 * * * *'

@description('Timer schedule for password rotation checks.')
param rotationSchedule string = '0 30 2 * * *'

@description('Maximum FC1 instance count.')
@minValue(1)
@maxValue(1000)
param maximumInstanceCount int = 10

@description('Memory allocated to each FC1 instance in MB.')
@allowed([
  512
  2048
  4096
])
param instanceMemoryMB int = 2048

@description('Tags applied to newly created resources.')
param tags object = {}

var servicePlanName = '${functionAppName}-plan'
var logAnalyticsWorkspaceName = '${functionAppName}-law'
var applicationInsightsName = '${functionAppName}-appi'
var deploymentContainerName = 'app-package-${uniqueString(resourceGroup().id, functionAppName)}'
var storagePrivateEndpointName = 'pe-${storageAccountName}-blob'
var keyVaultSecretsOfficerRoleDefinitionId = '/subscriptions/${keyVaultSubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
var storageBlobDataOwnerRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2024-05-01' existing = {
  name: virtualNetworkName
  scope: resourceGroup(virtualNetworkSubscriptionId, virtualNetworkResourceGroupName)
}

resource integrationSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  name: subnetName
  parent: virtualNetwork
}

resource privateEndpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  name: privateEndpointSubnetName
  parent: virtualNetwork
}

resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVaultName
  scope: resourceGroup(keyVaultSubscriptionId, keyVaultResourceGroupName)
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: 'Enabled'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  name: 'default'
  parent: storageAccount
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: deploymentContainerName
  parent: blobService
  properties: {
    publicAccess: 'None'
  }
}

resource storagePrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: storagePrivateEndpointName
  location: location
  tags: tags
  properties: {
    privateLinkServiceConnections: [
      {
        name: '${storagePrivateEndpointName}-connection'
        properties: {
          groupIds: [
            'blob'
          ]
          privateLinkServiceId: storageAccount.id
        }
      }
    ]
    subnet: {
      id: privateEndpointSubnet.id
    }
  }
}

resource servicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: servicePlanName
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
    zoneRedundant: false
  }
}

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  tags: tags
  properties: {
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    DisableIpMasking: false
    IngestionMode: 'LogAnalytics'
    Request_Source: 'rest'
    RetentionInDays: 30
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    clientAffinityEnabled: false
    functionAppConfig: {
      deployment: {
        storage: {
          authentication: {
            type: 'SystemAssignedIdentity'
          }
          type: 'blobContainer'
          value: '${storageAccount.properties.primaryEndpoints.blob}${deploymentContainer.name}'
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '8.0'
      }
      scaleAndConcurrency: {
        instanceMemoryMB: instanceMemoryMB
        maximumInstanceCount: maximumInstanceCount
      }
    }
    httpsOnly: true
    publicNetworkAccess: 'Enabled'
    serverFarmId: servicePlan.id
    siteConfig: {
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsights.properties.ConnectionString
        }
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccount.name
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'TaggingSchedule'
          value: taggingSchedule
        }
        {
          name: 'PasswordRotationSchedule'
          value: rotationSchedule
        }
        {
          name: 'ActiveDirectory__Server'
          value: activeDirectoryServer
        }
        {
          name: 'ActiveDirectory__Port'
          value: '636'
        }
        {
          name: 'ActiveDirectory__UseSsl'
          value: 'true'
        }
        {
          name: 'ActiveDirectory__AllowLdapFallback'
          value: string(allowLdapFallback)
        }
        {
          name: 'ActiveDirectory__LdapFallbackPort'
          value: string(ldapFallbackPort)
        }
        {
          name: 'ActiveDirectory__BindUsername'
          value: activeDirectoryBindUsername
        }
        {
          name: 'ActiveDirectory__BindUserDistinguishedName'
          value: activeDirectoryBindUserDistinguishedName
        }
        {
          name: 'ActiveDirectory__PageSize'
          value: '500'
        }
        {
          name: 'KeyVault__VaultUri'
          value: keyVault.properties.vaultUri
        }
        {
          name: 'KeyVault__CurrentPasswordSecretName'
          value: currentPasswordSecretName
        }
        {
          name: 'KeyVault__NextPasswordSecretName'
          value: nextPasswordSecretName
        }
        {
          name: 'PasswordRotation__RotateAfterDays'
          value: '25'
        }
        {
          name: 'PasswordRotation__PasswordLength'
          value: '40'
        }
        {
          name: 'PasswordRotation__SecretExpiryDays'
          value: '35'
        }
        {
          name: 'Tagging__RulesJson'
          value: string(taggingRules)
        }
      ]
      ftpsState: 'Disabled'
      http20Enabled: true
      minTlsVersion: '1.2'
      remoteDebuggingEnabled: false
      scmMinTlsVersion: '1.2'
    }
    virtualNetworkSubnetId: integrationSubnet.id
    vnetRouteAllEnabled: true
  }
}

resource deploymentStorageRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, storageBlobDataOwnerRoleDefinitionId)
  scope: storageAccount
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: storageBlobDataOwnerRoleDefinitionId
  }
}

module keyVaultRoleAssignment 'key-vault-role-assignment.bicep' = {
  scope: resourceGroup(keyVaultSubscriptionId, keyVaultResourceGroupName)
  params: {
    keyVaultName: keyVaultName
    principalId: functionApp.identity.principalId
    roleDefinitionId: keyVaultSecretsOfficerRoleDefinitionId
  }
}

output functionAppName string = functionApp.name
output functionAppPrincipalId string = functionApp.identity.principalId
output functionAppResourceId string = functionApp.id
output deploymentContainerName string = deploymentContainer.name
output storageAccountName string = storageAccount.name
