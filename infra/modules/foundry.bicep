// Azure AI Foundry account + default project + model deployment for the trend-synthesis agent.
// Grounding with Bing Search: the Bing resource is created MANUALLY by the customer in the Azure portal
// (that's where the Grounding with Bing terms of use are explicitly accepted). This module only references
// the existing resource and creates the Foundry connection to it.
param namePrefix string
param location string
param modelName string
param modelVersion string

@description('Name of the existing Grounding with Bing Search resource (created manually in the Azure portal)')
param bingResourceName string
param enableBingGrounding bool = true

@description('Resource group of the existing Grounding with Bing Search resource')
param bingResourceGroupName string = resourceGroup().name

var foundryAccountName = 'aif-${namePrefix}'
var foundryProjectName = 'proj-${namePrefix}'

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-04-01-preview' = {
  name: foundryAccountName
  location: location
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    allowProjectManagement: true
    customSubDomainName: foundryAccountName
    publicNetworkAccess: 'Enabled' // Bing Grounding requires normal network access — do not private-link this account
  }
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-04-01-preview' = {
  parent: foundryAccount
  name: foundryProjectName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {}
}

resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-04-01-preview' = {
  parent: foundryAccount
  name: modelName
  sku: {
    name: 'GlobalStandard'
    capacity: 30
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
  }
}

// Existing Grounding with Bing Search resource — created manually (see README "Manual prerequisite").
resource bingGrounding 'Microsoft.Bing/accounts@2025-05-01-preview' existing = {
  name: bingResourceName
  scope: resourceGroup(bingResourceGroupName)
}

// Connection from the Foundry account to the Bing Grounding resource.
resource bingConnection 'Microsoft.CognitiveServices/accounts/connections@2025-04-01-preview' = if (enableBingGrounding) {
  parent: foundryAccount
  name: 'bing-grounding-connection'
  properties: {
    category: 'GroundingWithBingSearch'
    authType: 'ApiKey'
    target: bingGrounding.properties.endpoint
    credentials: {
      key: bingGrounding.listKeys().key1
    }
  }
}

output accountName string = foundryAccount.name
output projectName string = foundryProject.name
output accountPrincipalId string = foundryAccount.identity.principalId
output projectEndpoint string = 'https://${foundryAccountName}.services.ai.azure.com/api/projects/${foundryProjectName}'
// Agents (data-plane) tool inputs require the connection ID scoped under the PROJECT, not the account, even though the connection resource itself is defined at account scope and shared to the project.
output bingConnectionId string = enableBingGrounding
  ? '${foundryProject.id}/connections/${bingConnection!.name}'
  : ''
