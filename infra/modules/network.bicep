// VNet + subnets for Private Endpoints and Function App VNet Integration.
// The Function App (Flex Consumption) uses its own delegated subnet; the daily orchestration runs on a
// multi-tenant Consumption Logic App, which requires no VNet injection, so no Logic App subnet exists.
param namePrefix string
param location string

var vnetName = 'vnet-${namePrefix}'
var peSubnetName = 'snet-privateendpoints'
var funcIntegrationSubnetName = 'snet-func-integration'

resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.20.0.0/16'
      ]
    }
    subnets: [
      {
        name: peSubnetName
        properties: {
          addressPrefix: '10.20.1.0/24'
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
      {
        name: funcIntegrationSubnetName
        properties: {
          addressPrefix: '10.20.3.0/24'
          delegations: [
            {
              name: 'delegation'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
    ]
  }
}

// Private DNS zones for storage sub-resources (blob + table)
resource blobDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.blob.${environment().suffixes.storage}'
  location: 'global'
}

resource tableDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.table.${environment().suffixes.storage}'
  location: 'global'
}

// Required for the Logic App Standard's AzureWebJobsStorage (jobs host queue coordination) — storage account has public access disabled.
resource queueDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.queue.${environment().suffixes.storage}'
  location: 'global'
}

resource blobDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: blobDnsZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    registrationEnabled: false
  }
}

resource tableDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: tableDnsZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    registrationEnabled: false
  }
}

resource queueDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: queueDnsZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    registrationEnabled: false
  }
}

output vnetId string = vnet.id
output peSubnetId string = vnet.properties.subnets[0].id
output funcIntegrationSubnetId string = vnet.properties.subnets[1].id
output blobDnsZoneId string = blobDnsZone.id
output tableDnsZoneId string = tableDnsZone.id
output queueDnsZoneId string = queueDnsZone.id
