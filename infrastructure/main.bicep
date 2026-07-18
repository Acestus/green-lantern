// Azure Functions Template - .NET
// Deploys one function app, one data UMI, one shared storage account, and one static web app.

// ============================================================================
// Parameters
// ============================================================================

@description('Project name used in resource naming')
param projectName string = 'functemplate'

@description('Environment (dev, prd)')
@allowed(['dev', 'prd'])
param environment string

@description('Azure region short name')
param region string = 'usw2'

@description('Instance number for uniqueness')
param instanceNumber string = '001'

@description('Resource tags')
param tags object

// ============================================================================
// Variables
// ============================================================================

var cafName = '${projectName}-${environment}-${region}-${instanceNumber}'
var location = resourceGroup().location
var dataUmiName = 'umi-${projectName}-${environment}-${region}-dat-${instanceNumber}'
var staticWebAppName = 'swa-${projectName}-${environment}-${region}-${instanceNumber}'
var functionPlanName = 'plan-${projectName}-${environment}-${region}-${instanceNumber}'
var helloFunctionName = 'func-${projectName}-hello-${environment}-${region}-${instanceNumber}'
var appInsightsName = 'appi-${projectName}-${environment}-${region}-${instanceNumber}'

// Storage account name (max 24 chars, lowercase, no hyphens)
var storageAccountName = 'st${replace(cafName, '-', '')}'
var storageAccountNameSafe = length(storageAccountName) > 24 ? substring(storageAccountName, 0, 24) : storageAccountName
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccountNameSafe};AccountKey=${listKeys(storageAccount.id, storageAccount.apiVersion).keys[0].value};EndpointSuffix=core.windows.net'

resource dataUmi 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: dataUmiName
  location: location
  tags: tags
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountNameSafe
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'
  }
}

resource functionPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: functionPlanName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {
    reserved: true
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}

resource helloWorldFunctionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: helloFunctionName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${dataUmi.id}': {}
    }
  }
  properties: {
    serverFarmId: functionPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'AzureWebJobsStorage'
          value: storageConnectionString
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'APPINSIGHTS_APP_ID'
          value: appInsights.properties.AppId
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
      ]
      cors: {
        allowedOrigins: [
          '*'
        ]
        supportCredentials: false
      }
    }
  }
}

resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: staticWebAppName
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

// ============================================================================
// Outputs
// ============================================================================

output storageAccountName string = storageAccount.name
output helloWorldFunctionAppName string = helloWorldFunctionApp.name
output dataUmiResourceId string = dataUmi.id
output staticWebAppName string = staticWebApp.name
output staticWebAppDefaultHostname string = staticWebApp.properties.defaultHostname
output appInsightsName string = appInsights.name
