using 'main.bicep'

param projectName = 'functemplate'
param environment = 'prd'
param region = 'usw2'
param instanceNumber = '001'

param tags = {
  ManagedBy: 'https://github.com/Acestus/template-functions'
  CreatedBy: '''template-owner'''
  Environment: 'Production'
  Subscription: 'acestus'
  Project: 'Azure Functions Template'
  CAFName: '${projectName}-${environment}-${region}-${instanceNumber}'
}
