/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
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

@description('Resource group containing the existing Key Vault.')
param keyVaultResourceGroupName string

@description('Subscription containing the existing Key Vault.')
param keyVaultSubscriptionId string = subscription().subscriptionId

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

@description('Maximum FC1 instance count per function scale group. Runs are additionally serialized by Blob leases.')
@minValue(1)
@maxValue(1000)
param maximumInstanceCount int = 1

@description('Memory allocated to each FC1 instance in MB.')
@allowed([
  512
  2048
  4096
])
param instanceMemoryMB int = 2048

@description('Tags applied to newly created resources.')
param tags object = {}

@description('Name of the Flex Consumption (FC1) App Service plan.')
var servicePlanName = '${functionAppName}-plan'
@description('Name of the Log Analytics workspace that stores Application Insights telemetry.')
var logAnalyticsWorkspaceName = '${functionAppName}-law'
@description('Name of the workspace-based Application Insights component.')
var applicationInsightsName = '${functionAppName}-appi'
@description('Deterministic, deployment-specific blob container name for Flex Consumption deployment packages.')
var deploymentContainerName = 'app-package-${uniqueString(resourceGroup().id, functionAppName)}'
@description('Name of the private endpoint for the storage Blob service.')
var storagePrivateEndpointName = 'pe-${storageAccountName}-blob'
@description('Name of the private endpoint for the storage Queue service.')
var storageQueuePrivateEndpointName = 'pe-${storageAccountName}-queue'
@description('Key Vault Secrets Officer role definition ID in the Key Vault subscription, which may differ from the deployment subscription.')
var keyVaultSecretsOfficerRoleDefinitionId = '/subscriptions/${keyVaultSubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
@description('Storage Blob Data Contributor role definition ID.')
var storageBlobDataContributorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
@description('Storage Queue Data Contributor role definition ID.')
var storageQueueDataContributorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
@description('Storage queues that receive manually requested device-tagging and password-rotation runs from the management API.')
var requestQueueNames = [
  'device-tagging-requests'
  'password-rotation-requests'
]

@description('Existing virtual network that hosts the integration and private endpoint subnets; referenced, never modified.')
resource virtualNetwork 'Microsoft.Network/virtualNetworks@2024-05-01' existing = {
  name: virtualNetworkName
  scope: resourceGroup(virtualNetworkSubscriptionId, virtualNetworkResourceGroupName)
}

@description('Existing subnet delegated to Microsoft.App/environments for Function App VNet integration.')
resource integrationSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  name: subnetName
  parent: virtualNetwork
}

@description('Existing subnet that hosts the storage private endpoints.')
resource privateEndpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  name: privateEndpointSubnetName
  parent: virtualNetwork
}

@description('Existing Key Vault that stores the current and staged Active Directory passwords; referenced, never modified.')
resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVaultName
  scope: resourceGroup(keyVaultSubscriptionId, keyVaultResourceGroupName)
}

@description('Function storage account for host storage, deployment packages, the membership ledger, run traces, and run-request queues. Public network access is disabled.')
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

@description('Blob service of the Function storage account.')
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  name: 'default'
  parent: storageAccount
}

@description('Private container that stores the Flex Consumption deployment package.')
resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: deploymentContainerName
  parent: blobService
  properties: {
    publicAccess: 'None'
  }
}

@description('Private container that records the Entra group and administrative-unit memberships owned by HybridDeviceTagger.')
resource membershipLedgerContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: 'membership-ledger'
  parent: blobService
  properties: {
    publicAccess: 'None'
  }
}

@description('Private container that stores device-tagging run traces.')
resource runTraceContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: 'run-traces'
  parent: blobService
  properties: {
    publicAccess: 'None'
  }
}

@description('Queue service of the Function storage account.')
resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  name: 'default'
  parent: storageAccount
}

@description('Queues that carry manually requested operation runs.')
resource requestQueues 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = [for queueName in requestQueueNames: {
  name: queueName
  parent: queueService
}]

@description('Private endpoint that exposes the storage Blob service in the private endpoint subnet.')
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

@description('Private endpoint that exposes the storage Queue service in the private endpoint subnet.')
resource storageQueuePrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: storageQueuePrivateEndpointName
  location: location
  tags: tags
  properties: {
    privateLinkServiceConnections: [
      {
        name: '${storageQueuePrivateEndpointName}-connection'
        properties: {
          groupIds: [
            'queue'
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

@description('Linux Flex Consumption (FC1) plan that hosts the Function App.')
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

@description('Log Analytics workspace with 30-day retention for Function telemetry.')
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

@description('Workspace-based Application Insights component for Function monitoring.')
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

@description('Linux .NET 8 isolated Function App with a system-assigned identity, VNet integration, identity-based deployment storage, and the HybridDeviceTagger configuration.')
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

@description('App Service Authentication that requires Entra tokens for the management API audience, except for the anonymous authentication metadata endpoint.')
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

@description('Grants the Function App identity Storage Blob Data Contributor on the storage account.')
resource storageBlobRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, storageBlobDataContributorRoleDefinitionId)
  scope: storageAccount
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: storageBlobDataContributorRoleDefinitionId
  }
}

@description('Grants the Function App identity Storage Queue Data Contributor on the storage account.')
resource storageQueueRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, storageQueueDataContributorRoleDefinitionId)
  scope: storageAccount
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: storageQueueDataContributorRoleDefinitionId
  }
}

// The role is scoped to the two password secrets only; other secrets in a shared vault stay
// inaccessible to the Function App. Both secrets must exist before deployment.
@description('Grants the Function App identity Key Vault Secrets Officer on the current and staged password secrets only.')
module keyVaultRoleAssignment 'key-vault-role-assignment.bicep' = {
  scope: resourceGroup(keyVaultSubscriptionId, keyVaultResourceGroupName)
  params: {
    keyVaultName: keyVaultName
    secretNames: [
      currentPasswordSecretName
      nextPasswordSecretName
    ]
    principalId: functionApp.identity.principalId
    roleDefinitionId: keyVaultSecretsOfficerRoleDefinitionId
  }
}

@description('Name of the deployed Function App.')
output functionAppName string = functionApp.name
@description('Object ID of the Function App system-assigned managed identity, used by the installer for Microsoft Graph permissions.')
output functionAppPrincipalId string = functionApp.identity.principalId
@description('Resource ID of the deployed Function App.')
output functionAppResourceId string = functionApp.id
@description('Name of the deployment package container.')
output deploymentContainerName string = deploymentContainer.name
@description('Name of the Function storage account.')
output storageAccountName string = storageAccount.name
