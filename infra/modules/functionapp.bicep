// Azure Functions .NET 10 isolated worker Flex Consumption app, system-assigned identity, optional VNet integration.
param namePrefix string
param location string

@description('Delegated subnet for outbound VNet integration. Empty = no VNet integration (public outbound).')
param integrationSubnetId string = ''
param appInsightsConnectionString string
param deploymentStorageAccountName string
param foundryProjectEndpoint string
param foundryModelDeploymentName string
param foundryBingConnectionId string
param enableBingGrounding bool = true

var functionAppName = 'func-${namePrefix}'
var flexPlanName = 'plan-${namePrefix}-flex'

resource deploymentStorage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: deploymentStorageAccountName
}

resource flexPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: flexPlanName
  location: location
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  kind: 'functionapp,linux'
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: flexPlan.id
    httpsOnly: true
    virtualNetworkSubnetId: empty(integrationSubnetId) ? null : integrationSubnetId
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${deploymentStorage.properties.primaryEndpoints.blob}app-package'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
    siteConfig: {
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'AzureWebJobsStorage__accountName'
          value: deploymentStorageAccountName
        }
        {
          name: 'Storage__AccountName'
          value: deploymentStorageAccountName
        }
        {
          name: 'Foundry__ProjectEndpoint'
          value: foundryProjectEndpoint
        }
        {
          name: 'Foundry__ModelDeploymentName'
          value: foundryModelDeploymentName
        }
        {
          name: 'Foundry__BingConnectionId'
          value: enableBingGrounding ? foundryBingConnectionId : ''
        }
      ]
    }
  }
}

output functionAppName string = functionApp.name
output principalId string = functionApp.identity.principalId
