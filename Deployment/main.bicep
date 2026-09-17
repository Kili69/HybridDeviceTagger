/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

targetScope = 'resourceGroup'

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

@description('Create the Key Vault in this deployment instead of referencing an existing vault.')
param createKeyVault bool = false

@description('Resource group containing the existing Key Vault.')
param keyVaultResourceGroupName string

@description('Subscription containing the existing Key Vault.')
param keyVaultSubscriptionId string = subscription().subscriptionId

@description('Optional resource ID of an existing Key Vault private DNS zone linked to the virtual network.')
param keyVaultPrivateDnsZoneResourceId string = ''

@description('Forest DNS name used for discovery, or the DNS name of a specific domain controller when discovery is unavailable.')
param activeDirectoryServer string

@description('Allow read and bind operations to fall back to unencrypted LDAP when LDAPS is unavailable.')
param allowLdapFallback bool = false

@description('Port used for unencrypted LDAP fallback.')
@minValue(1)
@maxValue(65535)
param ldapFallbackPort int = 389

@description('Optional path in the application package to a DER or PEM private LDAP trust anchor.')
param activeDirectoryTrustedCertificatePath string = ''

@description('UPN or DOMAIN\\user name used to bind to Active Directory.')
param activeDirectoryBindUsername string

@description('Distinguished name of the Active Directory service account whose password is rotated.')
param activeDirectoryBindUserDistinguishedName string

@description('Application (client) ID of the single-tenant Entra app registration protecting the management API.')
@minLength(36)
@maxLength(36)
param managementApiClientId string

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
var keyVaultPrivateEndpointName = 'pe-${keyVaultName}'
var keyVaultPrivateDnsZoneName = 'privatelink.vaultcore.azure.net'
var createKeyVaultPrivateDnsZone = createKeyVault && empty(keyVaultPrivateDnsZoneResourceId)
var effectiveKeyVaultPrivateDnsZoneResourceId = empty(keyVaultPrivateDnsZoneResourceId)
  ? resourceId('Microsoft.Network/privateDnsZones', keyVaultPrivateDnsZoneName)
  : keyVaultPrivateDnsZoneResourceId
var keyVaultUri = 'https://${keyVaultName}${environment().suffixes.keyvaultDns}'
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

resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' = if (createKeyVault) {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    accessPolicies: []
    enablePurgeProtection: true
    enableRbacAuthorization: true
    enableSoftDelete: true
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
    networkAcls: {
      bypass: 'None'
      defaultAction: 'Deny'
      ipRules: []
      virtualNetworkRules: []
    }
    publicNetworkAccess: 'Disabled'
    sku: {
      family: 'A'
      name: 'standard'
    }
    softDeleteRetentionInDays: 90
    tenantId: subscription().tenantId
  }
}

resource keyVaultPrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = if (createKeyVaultPrivateDnsZone) {
  name: keyVaultPrivateDnsZoneName
  location: 'global'
  tags: tags
}

resource keyVaultPrivateDnsVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = if (createKeyVaultPrivateDnsZone) {
  name: '${functionAppName}-keyvault-link'
  parent: keyVaultPrivateDnsZone
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: virtualNetwork.id
    }
  }
}

resource keyVaultPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = if (createKeyVault) {
  name: keyVaultPrivateEndpointName
  location: location
  tags: tags
  properties: {
    privateLinkServiceConnections: [
      {
        name: '${keyVaultPrivateEndpointName}-connection'
        properties: {
          groupIds: [
            'vault'
          ]
          privateLinkServiceId: keyVault.id
        }
      }
    ]
    subnet: {
      id: privateEndpointSubnet.id
    }
  }
}

resource keyVaultPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = if (createKeyVault) {
  name: 'default'
  parent: keyVaultPrivateEndpoint
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'keyVaultPrivateDnsZone'
        properties: {
          privateDnsZoneId: effectiveKeyVaultPrivateDnsZoneResourceId
        }
      }
    ]
  }
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
    publicNetworkAccess: 'Disabled'
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

resource membershipLedgerContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: 'membership-ledger'
  parent: blobService
  properties: {
    publicAccess: 'None'
  }
}

resource runTraceContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: 'run-traces'
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
          name: 'ActiveDirectory__TrustedCertificatePath'
          value: activeDirectoryTrustedCertificatePath
        }
        {
          name: 'LDAPTLS_CACERT'
          value: empty(activeDirectoryTrustedCertificatePath)
            ? '/etc/ssl/certs/ca-certificates.crt'
            : '/home/site/wwwroot/${activeDirectoryTrustedCertificatePath}'
        }
        {
          name: 'LDAPTLS_REQCERT'
          value: 'DEMAND'
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
          value: keyVaultUri
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
          name: 'ManagementApi__ClientId'
          value: managementApiClientId
        }
        {
          name: 'Tagging__RulesJson'
          value: '[]'
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

resource functionAppAuthentication 'Microsoft.Web/sites/config@2022-09-01' = {
  name: 'authsettingsV2'
  parent: functionApp
  properties: {
    globalValidation: {
      excludedPaths: [
        '/api/management/authentication'
      ]
      requireAuthentication: true
      unauthenticatedClientAction: 'Return401'
    }
    httpSettings: {
      requireHttps: true
    }
    identityProviders: {
      azureActiveDirectory: {
        enabled: true
        registration: {
          clientId: managementApiClientId
          openIdIssuer: '${environment().authentication.loginEndpoint}${tenant().tenantId}/v2.0'
        }
        validation: {
          allowedAudiences: [
            managementApiClientId
            'api://${managementApiClientId}'
          ]
        }
      }
    }
    login: {
      tokenStore: {
        enabled: false
      }
    }
    platform: {
      enabled: true
      runtimeVersion: '~1'
    }
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

module existingKeyVaultRoleAssignment 'key-vault-role-assignment.bicep' = if (!createKeyVault) {
  scope: resourceGroup(keyVaultSubscriptionId, keyVaultResourceGroupName)
  params: {
    keyVaultName: keyVaultName
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: keyVaultSecretsOfficerRoleDefinitionId
  }
}

module newKeyVaultRoleAssignment 'key-vault-role-assignment.bicep' = if (createKeyVault) {
  scope: resourceGroup(keyVaultSubscriptionId, keyVaultResourceGroupName)
  params: {
    keyVaultName: keyVaultName
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: keyVaultSecretsOfficerRoleDefinitionId
  }
  dependsOn: [
    keyVault
  ]
}

output functionAppName string = functionApp.name
output functionAppPrincipalId string = functionApp.identity.principalId
output functionAppResourceId string = functionApp.id
output keyVaultUri string = keyVaultUri
output deploymentContainerName string = deploymentContainer.name
output storageAccountName string = storageAccount.name
