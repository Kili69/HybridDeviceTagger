/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

targetScope = 'resourceGroup'

@description('Name of the existing Key Vault.')
param keyVaultName string

@description('Names of the existing secrets to which the role is assigned.')
param secretNames string[]

@description('Object ID of the Function App managed identity.')
param principalId string

@description('Fully qualified role definition resource ID.')
param roleDefinitionId string

@description('Existing Key Vault that contains the password secrets; referenced, never modified.')
resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVaultName
}

@description('Existing secrets used as the role-assignment scopes. Each secret must exist before deployment.')
resource secrets 'Microsoft.KeyVault/vaults/secrets@2024-11-01' existing = [for secretName in secretNames: {
  name: secretName
  parent: keyVault
}]

@description('Secret-scoped role assignments for the managed identity, one per secret, with deterministic names for idempotent redeployment.')
resource roleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (secretName, index) in secretNames: {
  name: guid(secrets[index].id, principalId, roleDefinitionId)
  scope: secrets[index]
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: roleDefinitionId
  }
}]
