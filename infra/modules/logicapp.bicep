// Logic App Consumption (multi-tenant) — daily recurrence that drives ingestion + Foundry synthesis + Outlook delivery.
// Consumption is used instead of Standard/WS1 because this subscription's Azure Policy forces
// allowSharedKeyAccess=false on all storage accounts, and Standard/WS1 REQUIRES storage account
// key access for its Azure Files content share (unsupported to disable outside App Service Environment v3).
// Consumption has no customer storage account, so the shared-key policy does not apply.
param namePrefix string
param location string
param functionAppName string

@description('Recipient for the daily digest email')
param digestRecipient string

var logicAppName = 'logic-${namePrefix}'
var office365ConnectionName = 'office365-${namePrefix}'
var baseUrl = 'https://${functionAppName}.azurewebsites.net/api'
var funcHostKey = listKeys(resourceId('Microsoft.Web/sites/host', functionAppName, 'default'), '2022-03-01').functionKeys.default
var office365ApiId = '${subscription().id}/providers/Microsoft.Web/locations/${location}/managedApis/office365'

// Office 365 Outlook managed API connection. Created unauthenticated — a one-time interactive
// OAuth consent must be completed in the portal (Logic App > API connections > authorize) before the first send.
resource office365Connection 'Microsoft.Web/connections@2016-06-01' = {
  name: office365ConnectionName
  location: location
  properties: {
    displayName: 'office365'
    api: {
      id: office365ApiId
    }
  }
}

resource workflow 'Microsoft.Logic/workflows@2019-05-01' = {
  name: logicAppName
  location: location
  properties: {
    state: 'Enabled'
    definition: {
      '$schema': 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#'
      contentVersion: '1.0.0.0'
      parameters: {
        '$connections': {
          type: 'Object'
          defaultValue: {}
        }
      }
      triggers: {
        Daily_0600_MT: {
          type: 'Recurrence'
          recurrence: {
            frequency: 'Day'
            interval: 1
            schedule: {
              hours: ['6']
              minutes: [0]
            }
            timeZone: 'Mountain Standard Time'
          }
        }
      }
      actions: {
        Call_IngestFeeds: {
          type: 'Http'
          inputs: {
            method: 'POST'
            uri: '${baseUrl}/ingestfeeds'
            queries: {
              code: funcHostKey
            }
          }
        }
        Call_SynthesizeTrends: {
          type: 'Http'
          runAfter: {
            Call_IngestFeeds: ['Succeeded']
          }
          inputs: {
            method: 'POST'
            uri: '${baseUrl}/synthesizetrends'
            queries: {
              code: funcHostKey
            }
          }
        }
        Send_Outlook_Email: {
          type: 'ApiConnection'
          runAfter: {
            Call_SynthesizeTrends: ['Succeeded']
          }
          inputs: {
            host: {
              connection: {
                name: '@parameters(\'$connections\')[\'office365\'][\'connectionId\']'
              }
            }
            method: 'post'
            path: '/v2/Mail'
            body: {
              To: digestRecipient
              Subject: 'NM Daily Trend Digest — @{utcNow(\'yyyy-MM-dd\')}'
              Body: '@{body(\'Call_SynthesizeTrends\')?[\'html\']}'
              Importance: 'Normal'
            }
          }
        }
      }
      outputs: {}
    }
    parameters: {
      '$connections': {
        value: {
          office365: {
            connectionId: office365Connection.id
            connectionName: office365ConnectionName
            id: office365ApiId
          }
        }
      }
    }
  }
}

output logicAppName string = workflow.name
output office365ConnectionName string = office365Connection.name
