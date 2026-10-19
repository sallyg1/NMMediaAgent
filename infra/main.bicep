// Phase 1: Foundation infra for NM Trend Intelligence Agent.
// Deploy into an existing resource group: az deployment group create -g <rg> -f main.bicep
targetScope = 'resourceGroup'

@description('Short project prefix used to build resource names')
param projectName string = 'nmtrend'

@description('Environment token (dev/test/prod) appended to resource names')
param environmentName string = 'dev'

@description('Primary Azure region for all resources')
param location string = resourceGroup().location

@description('Azure OpenAI model to deploy for the Foundry agent')
param modelName string = 'gpt-4.1-mini'

@description('Model version to deploy')
param modelVersion string = '2025-04-14'

@description('Recipient email for the daily digest (Outlook distribution)')
param digestRecipient string

@description('Name of the existing Grounding with Bing Search resource. Create it manually in the Azure portal first so the customer explicitly accepts the Grounding with Bing terms of use.')
param bingResourceName string
@description('Attach Grounding with Bing Search to the Foundry project and agent')
param enableBingGrounding bool = true

@description('Resource group of the existing Grounding with Bing Search resource (defaults to this deployment\'s resource group)')
param bingResourceGroupName string = resourceGroup().name

@description('true: VNet, storage private endpoints + private DNS, storage public access disabled, Function App VNet integration (required where Azure Policy blocks storage public access). false: public endpoints, no VNet.')
param enablePrivateNetworking bool = false

var resourceToken = uniqueString(subscription().id, resourceGroup().id, projectName, environmentName)
var namePrefix = '${projectName}${environmentName}'

// ---------- Networking (VNet, subnets, Private DNS) — only when private networking is enabled ----------
module network 'modules/network.bicep' = if (enablePrivateNetworking) {
  name: 'network'
  params: {
    namePrefix: namePrefix
    location: location
  }
}

// ---------- Monitoring (Log Analytics + App Insights) ----------
module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    namePrefix: namePrefix
    location: location
  }
}

// ---------- Storage (Blob raw-landing + Table urlDedupCache; Entra ID auth only) ----------
module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    storageAccountName: take('st${namePrefix}${resourceToken}', 24)
    location: location
    enablePrivateNetworking: enablePrivateNetworking
    peSubnetId: enablePrivateNetworking ? network!.outputs.peSubnetId : ''
    blobDnsZoneId: enablePrivateNetworking ? network!.outputs.blobDnsZoneId : ''
    tableDnsZoneId: enablePrivateNetworking ? network!.outputs.tableDnsZoneId : ''
    queueDnsZoneId: enablePrivateNetworking ? network!.outputs.queueDnsZoneId : ''
  }
}

// ---------- Function App (.NET 10 isolated Flex Consumption, system-assigned identity, optional VNet integration) ----------
module functionApp 'modules/functionapp.bicep' = {
  name: 'functionApp'
  params: {
    namePrefix: namePrefix
    location: location
    integrationSubnetId: enablePrivateNetworking ? network!.outputs.funcIntegrationSubnetId : ''
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    deploymentStorageAccountName: storage.outputs.storageAccountName
    foundryProjectEndpoint: foundry.outputs.projectEndpoint
    foundryModelDeploymentName: modelName
    foundryBingConnectionId: foundry.outputs.bingConnectionId
    enableBingGrounding: enableBingGrounding
  }
}

// ---------- Logic App Consumption (multi-tenant, daily recurrence) ----------
module logicApp 'modules/logicapp.bicep' = {
  name: 'logicApp'
  params: {
    namePrefix: namePrefix
    location: location
    functionAppName: functionApp.outputs.functionAppName
    digestRecipient: digestRecipient
  }
}

// ---------- Azure AI Foundry account/project + model deployment + connection to the existing (manually created) Bing resource ----------
module foundry 'modules/foundry.bicep' = {
  name: 'foundry'
  params: {
    namePrefix: namePrefix
    location: location
    modelName: modelName
    modelVersion: modelVersion
    bingResourceName: bingResourceName
    bingResourceGroupName: bingResourceGroupName
    enableBingGrounding: enableBingGrounding
  }
}

// ---------- RBAC: Function App identity -> Storage; Foundry project identity -> Cognitive Services ----------
module rbac 'modules/rbac.bicep' = {
  name: 'rbac'
  params: {
    storageAccountName: storage.outputs.storageAccountName
    functionAppPrincipalId: functionApp.outputs.principalId
    foundryAccountName: foundry.outputs.accountName
  }
}

output storageAccountName string = storage.outputs.storageAccountName
output functionAppName string = functionApp.outputs.functionAppName
output logicAppName string = logicApp.outputs.logicAppName
output foundryAccountName string = foundry.outputs.accountName
output foundryProjectName string = foundry.outputs.projectName
