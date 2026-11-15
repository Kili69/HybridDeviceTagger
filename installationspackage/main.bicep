/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * Copyright 2026 Andreas Lucas
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 * SPDX-License-Identifier: Apache-2.0
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

@description('Optional resource ID of an existing Blob Storage private DNS zone linked to the virtual network.')
param storagePrivateDnsZoneResourceId string = ''

@description('Optional resource ID of an existing Queue Storage private DNS zone linked to the virtual network.')
param storageQueuePrivateDnsZoneResourceId string = ''

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

@description('Password secrets that do not exist yet in an existing Key Vault. They are created as unusable placeholders so that the managed identity can be authorized on the two password secrets only. List only missing secrets.')
param keyVaultPlaceholderSecretNames string[] = []

@description('Active Directory forest DNS name used by the Function for DNS SRV domain-controller discovery.')
param activeDirectoryDomain string

@description('Allow LDAPS to fall back to LDAP upgraded with StartTLS.')
param allowLdapFallback bool = true

@description('Port used for LDAP upgraded with StartTLS.')
@minValue(1)
@maxValue(65535)
param ldapFallbackPort int = 389

@description('Optional path in the application package to a DER or PEM private LDAP trust anchor.')
param activeDirectoryTrustedCertificatePath string = ''

@description('UPN or DOMAIN\\user name used to bind to Active Directory.')
param activeDirectoryBindUsername string

@description('Optional service-account distinguished name. When empty, the Function resolves it from the configured UPN in the forest.')
param activeDirectoryBindUserDistinguishedName string = ''

@description('Application (client) ID of the single-tenant Entra app registration protecting the management API.')
@minLength(36)
@maxLength(36)
param managementApiClientId string

@description('Current Active Directory password secret name.')
param currentPasswordSecretName string = 'hybrid-device-tagger-ad-password-current'

@description('Staged Active Directory password secret name.')
param nextPasswordSecretName string = 'hybrid-device-tagger-ad-password-next'

@description('Initial minutes between scheduled tagging runs; the configuration portal can change it at runtime.')
@minValue(5)
@maxValue(1440)
param taggingIntervalMinutes int = 15

@description('Initial password age in days at which the service-account password is changed; the configuration portal can change it at runtime.')
@minValue(1)
@maxValue(29)
param passwordRotationDays int = 25

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

@description('Name of the Flex Consumption hosting plan.')
var servicePlanName = '${functionAppName}-plan'
@description('Name of the Log Analytics workspace that stores Application Insights telemetry.')
var logAnalyticsWorkspaceName = '${functionAppName}-law'
@description('Name of the Application Insights component.')
var applicationInsightsName = '${functionAppName}-appi'
@description('Deterministic, deployment-specific blob container name for Function App packages.')
var deploymentContainerName = 'app-package-${uniqueString(resourceGroup().id, functionAppName)}'
@description('Name of the Blob Storage private endpoint.')
var storagePrivateEndpointName = 'pe-${storageAccountName}-blob'
@description('Blob Storage private DNS zone name for the current Azure cloud.')
var storagePrivateDnsZoneName = 'privatelink.blob.${environment().suffixes.storage}'
@description('Creates the Blob Storage private DNS zone when no existing zone is supplied.')
var createStoragePrivateDnsZone = empty(storagePrivateDnsZoneResourceId)
@description('Resource ID of the supplied or newly created Blob Storage private DNS zone.')
var effectiveStoragePrivateDnsZoneResourceId = empty(storagePrivateDnsZoneResourceId)
  ? resourceId('Microsoft.Network/privateDnsZones', storagePrivateDnsZoneName)
  : storagePrivateDnsZoneResourceId
@description('Name of the Queue Storage private endpoint.')
var storageQueuePrivateEndpointName = 'pe-${storageAccountName}-queue'
@description('Queue Storage private DNS zone name for the current Azure cloud.')
var storageQueuePrivateDnsZoneName = 'privatelink.queue.${environment().suffixes.storage}'
@description('Creates the Queue Storage private DNS zone when no existing zone is supplied.')
var createStorageQueuePrivateDnsZone = empty(storageQueuePrivateDnsZoneResourceId)
@description('Resource ID of the supplied or newly created Queue Storage private DNS zone.')
var effectiveStorageQueuePrivateDnsZoneResourceId = empty(storageQueuePrivateDnsZoneResourceId)
  ? resourceId('Microsoft.Network/privateDnsZones', storageQueuePrivateDnsZoneName)
  : storageQueuePrivateDnsZoneResourceId
@description('Storage queues that receive manually requested device-tagging and password-rotation runs from the management API.')
var requestQueueNames = [
  'device-tagging-requests'
  'password-rotation-requests'
]
@description('Name of the private endpoint of a newly created Key Vault.')
var keyVaultPrivateEndpointName = 'pe-${keyVaultName}'
@description('Key Vault private DNS zone name.')
var keyVaultPrivateDnsZoneName = 'privatelink.vaultcore.azure.net'
@description('Creates the Key Vault private DNS zone only for a new vault without a supplied existing zone.')
var createKeyVaultPrivateDnsZone = createKeyVault && empty(keyVaultPrivateDnsZoneResourceId)
@description('Resource ID of the supplied or newly created Key Vault private DNS zone.')
var effectiveKeyVaultPrivateDnsZoneResourceId = empty(keyVaultPrivateDnsZoneResourceId)
  ? resourceId('Microsoft.Network/privateDnsZones', keyVaultPrivateDnsZoneName)
  : keyVaultPrivateDnsZoneResourceId
@description('Data-plane URI of the selected Key Vault.')
var keyVaultUri = 'https://${keyVaultName}${environment().suffixes.keyvaultDns}'
@description('Key Vault Secrets Officer role definition in the Key Vault subscription.')
var keyVaultSecretsOfficerRoleDefinitionId = '/subscriptions/${keyVaultSubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
@description('Storage Blob Data Contributor role definition.')
var storageBlobDataContributorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
@description('Storage Queue Data Contributor role definition.')
var storageQueueDataContributorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
@description('Reader role definition.')
var readerRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'acdd72a7-3385-48ef-bd42-f606fba81ae7')
@description('Monitoring Metrics Publisher role definition ID used for Entra-authenticated telemetry ingestion.')
var monitoringMetricsPublisherRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '3913510d-42f4-4e42-8a64-420c390055eb')
@description('Monitoring Reader role definition ID used to query the activity log.')
var monitoringReaderRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '43d0d8ad-25c7-4714-9337-8ba259a9fe05')
@description('Static configuration-portal paths that load before sign-in; they contain no data.')
var portalAnonymousPaths = [
  '/api/portal'
  '/api/portal/'
  '/api/portal/index.html'
  '/api/portal/app.js'
  '/api/portal/app.css'
  '/api/portal/config.json'
]

@description('Existing virtual network, which is referenced but not modified.')
resource virtualNetwork 'Microsoft.Network/virtualNetworks@2024-05-01' existing = {
  name: virtualNetworkName
  scope: resourceGroup(virtualNetworkSubscriptionId, virtualNetworkResourceGroupName)
}

@description('Existing subnet used for Flex Consumption VNet integration.')
resource integrationSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  name: subnetName
  parent: virtualNetwork
}

@description('Existing subnet that hosts the Storage and Key Vault private endpoints.')
resource privateEndpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  name: privateEndpointSubnetName
  parent: virtualNetwork
}

@description('Private Key Vault with RBAC authorization, soft delete, and purge protection, created only when createKeyVault is true.')
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

@description('Key Vault private DNS zone, created for a new vault when no existing zone is supplied.')
resource keyVaultPrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = if (createKeyVaultPrivateDnsZone) {
  name: keyVaultPrivateDnsZoneName
  location: 'global'
  tags: tags
}

@description('Links the created Key Vault private DNS zone to the virtual network.')
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

@description('Private endpoint of a newly created Key Vault.')
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

@description('Registers the Key Vault private endpoint address in the Key Vault private DNS zone.')
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

@description('Private StorageV2 account for Functions host storage, deployment packages, the membership ledger, run traces, and run-request queues. Public network access is disabled.')
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

@description('Blob service of the storage account.')
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  name: 'default'
  parent: storageAccount
}

@description('Private blob container that holds Function App deployment packages.')
resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: deploymentContainerName
  parent: blobService
  properties: {
    publicAccess: 'None'
  }
}

@description('Private blob container for the ledger of group and administrative-unit memberships owned by HybridDeviceTagger.')
resource membershipLedgerContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: 'membership-ledger'
  parent: blobService
  properties: {
    publicAccess: 'None'
  }
}

@description('Private blob container for device-tagging run traces.')
resource runTraceContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: 'run-traces'
  parent: blobService
  properties: {
    publicAccess: 'None'
  }
}

@description('Private endpoint for the storage Blob service.')
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

@description('Blob Storage private DNS zone, created when no existing zone is supplied.')
resource storagePrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = if (createStoragePrivateDnsZone) {
  name: storagePrivateDnsZoneName
  location: 'global'
  tags: tags
}

@description('Links the created Blob Storage private DNS zone to the virtual network.')
resource storagePrivateDnsVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = if (createStoragePrivateDnsZone) {
  name: '${functionAppName}-storage-link'
  parent: storagePrivateDnsZone
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: virtualNetwork.id
    }
  }
}

@description('Registers the Blob private endpoint address in the Blob Storage private DNS zone.')
resource storagePrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  name: 'default'
  parent: storagePrivateEndpoint
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'storagePrivateDnsZone'
        properties: {
          privateDnsZoneId: effectiveStoragePrivateDnsZoneResourceId
        }
      }
    ]
  }
  dependsOn: [
    storagePrivateDnsZone
  ]
}

@description('Queue service of the storage account.')
resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  name: 'default'
  parent: storageAccount
}

@description('Private queues that carry management-API run requests to the queue-triggered functions.')
resource requestQueues 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = [for queueName in requestQueueNames: {
  name: queueName
  parent: queueService
}]

@description('Private endpoint for the storage Queue service, used by the queue triggers and the management API.')
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

@description('Queue Storage private DNS zone, created when no existing zone is supplied.')
resource storageQueuePrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = if (createStorageQueuePrivateDnsZone) {
  name: storageQueuePrivateDnsZoneName
  location: 'global'
  tags: tags
}

@description('Links the created Queue Storage private DNS zone to the virtual network.')
resource storageQueuePrivateDnsVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = if (createStorageQueuePrivateDnsZone) {
  name: '${functionAppName}-storage-queue-link'
  parent: storageQueuePrivateDnsZone
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: virtualNetwork.id
    }
  }
}

@description('Registers the Queue private endpoint address in the Queue Storage private DNS zone.')
resource storageQueuePrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  name: 'default'
  parent: storageQueuePrivateEndpoint
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'storageQueuePrivateDnsZone'
        properties: {
          privateDnsZoneId: effectiveStorageQueuePrivateDnsZoneResourceId
        }
      }
    ]
  }
  dependsOn: [
    storageQueuePrivateDnsZone
  ]
}

@description('Linux Flex Consumption (FC1) hosting plan.')
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

@description('Log Analytics workspace with 30-day retention for Application Insights telemetry and the HybridDeviceTagger activity log.')
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

@description('Workspace-based Application Insights component for Function App telemetry.')
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    DisableIpMasking: false
    DisableLocalAuth: true
    IngestionMode: 'LogAnalytics'
    Request_Source: 'rest'
    RetentionInDays: 30
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

@description('Linux .NET 8 isolated Function App on Flex Consumption with a system-assigned identity, identity-based storage access, VNet integration that routes all traffic, and the HybridDeviceTagger application settings.')
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
          name: 'APPLICATIONINSIGHTS_AUTHENTICATION_STRING'
          value: 'Authorization=AAD'
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
          name: 'Tagging__IntervalMinutes'
          value: string(taggingIntervalMinutes)
        }
        {
          name: 'ActiveDirectory__Domain'
          value: activeDirectoryDomain
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
          value: string(passwordRotationDays)
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
          name: 'ManagementAuthorization__FunctionAppResourceId'
          value: resourceId('Microsoft.Web/sites', functionAppName)
        }
        {
          name: 'ManagementApi__TenantId'
          value: tenant().tenantId
        }
        {
          name: 'ManagementApi__AuthorityHost'
          value: environment().authentication.loginEndpoint
        }
        {
          name: 'Monitoring__ApplicationInsightsResourceId'
          value: applicationInsights.id
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
    vnetContentShareEnabled: true
    vnetRouteAllEnabled: true
  }
}

@description('App Service Authentication that requires Entra tokens for the management API audience on all paths except /api/management/authentication and the static configuration-portal files, and returns HTTP 401 for unauthenticated requests.')
resource functionAppAuthentication 'Microsoft.Web/sites/config@2022-09-01' = {
  name: 'authsettingsV2'
  parent: functionApp
  properties: {
    globalValidation: {
      excludedPaths: concat([
        '/api/management/authentication'
      ], portalAnonymousPaths)
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

@description('Blob read and write access for host storage, deployment packages, ledger, traces, status, and leases.')
resource storageBlobRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, storageBlobDataContributorRoleDefinitionId)
  scope: storageAccount
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: storageBlobDataContributorRoleDefinitionId
  }
}

@description('Queue read and write access for the run-request queues and the queue triggers.')
resource storageQueueRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, storageQueueDataContributorRoleDefinitionId)
  scope: storageAccount
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: storageQueueDataContributorRoleDefinitionId
  }
}

@description('Monitoring Metrics Publisher role of the Function identity on Application Insights; local authentication is disabled, so only Entra-authenticated telemetry, including the activity log, is accepted.')
resource applicationInsightsMetricsPublisherRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(applicationInsights.id, functionApp.id, monitoringMetricsPublisherRoleDefinitionId)
  scope: applicationInsights
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: monitoringMetricsPublisherRoleDefinitionId
  }
}

@description('Monitoring Reader role of the Function identity on Application Insights, used by the configuration portal to query the activity log.')
resource applicationInsightsMonitoringReaderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(applicationInsights.id, functionApp.id, monitoringReaderRoleDefinitionId)
  scope: applicationInsights
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: monitoringReaderRoleDefinitionId
  }
}

@description('Reader role of the Function identity on its own Function App, used to evaluate management API authorization against Function App role assignments.')
resource functionAppReaderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(functionApp.id, readerRoleDefinitionId)
  scope: functionApp
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: readerRoleDefinitionId
  }
}

// An existing vault may be shared, so the role is scoped to the two password secrets. Missing
// secrets are created as marked placeholders first.
@description('Key Vault Secrets Officer role on the two password secrets of an existing, possibly shared vault.')
module existingKeyVaultRoleAssignment 'key-vault-role-assignment.bicep' = if (!createKeyVault) {
  scope: resourceGroup(keyVaultSubscriptionId, keyVaultResourceGroupName)
  params: {
    keyVaultName: keyVaultName
    secretNames: [
      currentPasswordSecretName
      nextPasswordSecretName
    ]
    placeholderSecretNames: keyVaultPlaceholderSecretNames
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: keyVaultSecretsOfficerRoleDefinitionId
  }
}

@description('Vault-scoped Key Vault Secrets Officer role on a vault created for HybridDeviceTagger.')
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

@description('Name of the deployed Function App.')
output functionAppName string = functionApp.name

@description('URL of the configuration portal for Function App Owners and Contributors.')
output configurationPortalUrl string = 'https://${functionApp.properties.defaultHostName}/api/portal'
@description('Object ID of the Function App system-assigned managed identity.')
output functionAppPrincipalId string = functionApp.identity.principalId
@description('Resource ID of the deployed Function App.')
output functionAppResourceId string = functionApp.id
@description('Data-plane URI of the selected Key Vault.')
output keyVaultUri string = keyVaultUri
@description('Name of the blob container that holds deployment packages.')
output deploymentContainerName string = deploymentContainer.name
@description('Name of the storage account.')
output storageAccountName string = storageAccount.name
