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

@description('Name of the existing Key Vault.')
param keyVaultName string

@description('Secrets to which the role is assigned. An empty list assigns the role on the whole vault, which is intended only for a vault dedicated to HybridDeviceTagger.')
param secretNames string[] = []

@description('Missing secrets from secretNames that are created as unusable placeholders so that the secret-scoped role can be assigned. Existing secrets must never be listed, because their value would be replaced.')
param placeholderSecretNames string[] = []

@description('Random placeholder value. It cannot authenticate to Active Directory.')
@secure()
param placeholderValue string = newGuid()

@description('Object ID of the role-assignment principal.')
param principalId string

@description('Directory object type of the role-assignment principal.')
@allowed([
  'ServicePrincipal'
  'User'
])
param principalType string = 'ServicePrincipal'

@description('Fully qualified role definition resource ID.')
param roleDefinitionId string

@description('Content type that marks a placeholder secret. The Function replaces only secrets with this marker during credential initialization.')
var placeholderContentType = 'application/vnd.hybriddevicetagger.placeholder'

@description('Existing Key Vault that contains the password secrets.')
resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVaultName
}

@description('Placeholder secrets for missing password secrets, so that the role can be scoped to them before the Function initializes or rotates the password.')
resource placeholderSecrets 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = [for secretName in placeholderSecretNames: {
  name: secretName
  parent: keyVault
  properties: {
    value: placeholderValue
    contentType: placeholderContentType
  }
}]

@description('Password secrets used as role-assignment scopes.')
resource secrets 'Microsoft.KeyVault/vaults/secrets@2024-11-01' existing = [for secretName in secretNames: {
  name: secretName
  parent: keyVault
}]

@description('Secret-scoped role assignments with deterministic names for idempotent redeployment.')
resource secretRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (secretName, index) in secretNames: {
  name: guid(secrets[index].id, principalId, roleDefinitionId)
  scope: secrets[index]
  properties: {
    principalId: principalId
    principalType: principalType
    roleDefinitionId: roleDefinitionId
  }
  dependsOn: [
    placeholderSecrets
  ]
}]

@description('Vault-scoped role assignment, used only for a vault dedicated to HybridDeviceTagger.')
resource vaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (empty(secretNames)) {
  name: guid(keyVault.id, principalId, roleDefinitionId)
  scope: keyVault
  properties: {
    principalId: principalId
    principalType: principalType
    roleDefinitionId: roleDefinitionId
  }
}