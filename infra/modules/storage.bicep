// Storage account for the ingestion pipeline: Blob raw-landing container + Table urlDedupCache.
// Shared-key access is always disabled; all callers use Entra ID (managed identity).
// enablePrivateNetworking=true: public network access Disabled + private endpoints (blob, table, queue) —
// required where Azure Policy blocks storage public access. false: public endpoint, no private endpoints.
param storageAccountName string
param location string
param enablePrivateNetworking bool = false
param peSubnetId string = ''
param blobDnsZoneId string = ''
param tableDnsZoneId string = ''
param queueDnsZoneId string = ''

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    publicNetworkAccess: enablePrivateNetworking ? 'Disabled' : 'Enabled'
    networkAcls: {
      defaultAction: enablePrivateNetworking ? 'Deny' : 'Allow'
      bypass: 'AzureServices'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource rawLandingContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'raw-landing'
  properties: {
    publicAccess: 'None'
  }
}

// Deployment package container for the Function App (Flex Consumption) — reuses this storage account
// instead of provisioning a second one.
resource appPackageContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'app-package'
  properties: {
    publicAccess: 'None'
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource urlDedupTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: 'urlDedupCache'
}

// Required so the Logic App Standard's AzureWebJobsStorage (jobs host) can reach the queue sub-resource privately.
resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

// Private Endpoint: blob sub-resource
resource peBlob 'Microsoft.Network/privateEndpoints@2023-11-01' = if (enablePrivateNetworking) {
  name: '${storageAccountName}-pe-blob'
  location: location
  properties: {
    subnet: {
      id: peSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'blobConnection'
        properties: {
          privateLinkServiceId: storage.id
          groupIds: [
            'blob'
          ]
        }
      }
    ]
  }
}

resource peBlobDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = if (enablePrivateNetworking) {
  parent: peBlob
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'blob'
        properties: {
          privateDnsZoneId: blobDnsZoneId
        }
      }
    ]
  }
}

// Private Endpoint: table sub-resource
resource peTable 'Microsoft.Network/privateEndpoints@2023-11-01' = if (enablePrivateNetworking) {
  name: '${storageAccountName}-pe-table'
  location: location
  properties: {
    subnet: {
      id: peSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'tableConnection'
        properties: {
          privateLinkServiceId: storage.id
          groupIds: [
            'table'
          ]
        }
      }
    ]
  }
}

resource peTableDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = if (enablePrivateNetworking) {
  parent: peTable
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'table'
        properties: {
          privateDnsZoneId: tableDnsZoneId
        }
      }
    ]
  }
}

// Private Endpoint: queue sub-resource (Logic App Standard AzureWebJobsStorage)
resource peQueue 'Microsoft.Network/privateEndpoints@2023-11-01' = if (enablePrivateNetworking) {
  name: '${storageAccountName}-pe-queue'
  location: location
  properties: {
    subnet: {
      id: peSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'queueConnection'
        properties: {
          privateLinkServiceId: storage.id
          groupIds: [
            'queue'
          ]
        }
      }
    ]
  }
}

resource peQueueDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = if (enablePrivateNetworking) {
  parent: peQueue
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'queue'
        properties: {
          privateDnsZoneId: queueDnsZoneId
        }
      }
    ]
  }
}

output storageAccountId string = storage.id
output storageAccountName string = storage.name
