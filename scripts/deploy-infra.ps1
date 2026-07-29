<#
.SYNOPSIS
    Deploys Azure infrastructure using Bicep templates and Azure Deployment Stacks.

.DESCRIPTION
    This script deploys the Azure Functions infrastructure using deployment stacks
    for better lifecycle management of Azure resources.

.PARAMETER Environment
    Target environment (dev or prd)

.EXAMPLE
    ./deploy-infra.ps1 -Environment dev
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dev', 'prd')]
    [string]$Environment
)

$ErrorActionPreference = 'Stop'

$TargetSubscriptionId = 'df64929f-810d-4176-8097-35cd05cae10d'

# Configuration
$ProjectName = 'functemplate'
$Region = 'usw2'
$InstanceNumber = '001'
$CafName = "$ProjectName-$Environment-$Region-$InstanceNumber"
$ResourceGroupName = "rg-$CafName"
$StackName = "stack-$CafName"
$TemplateFile = Join-Path $PSScriptRoot '..' 'infrastructure' 'main.bicep'
$ParameterFile = Join-Path $PSScriptRoot '..' 'infrastructure' "main.$Environment.bicepparam"

$CurrentContext = Get-AzContext
if (-not $CurrentContext) {
    throw "No Azure context found. Sign in first, then rerun this script."
}

if ($CurrentContext.Subscription.Id -ne $TargetSubscriptionId) {
    throw "This template only deploys to subscription $TargetSubscriptionId (acestus). Current subscription: $($CurrentContext.Subscription.Id)"
}

Write-Host "Deploying infrastructure for environment: $Environment" -ForegroundColor Cyan
Write-Host "Resource Group: $ResourceGroupName" -ForegroundColor Gray
Write-Host "Stack Name: $StackName" -ForegroundColor Gray

# Ensure resource group exists
$rg = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction SilentlyContinue
if (-not $rg) {
    Write-Host "Creating resource group: $ResourceGroupName" -ForegroundColor Yellow
    New-AzResourceGroup -Name $ResourceGroupName -Location 'westus2' -Tags @{
        Environment = $Environment
        Project     = 'Azure Functions Template'
        ManagedBy   = 'https://github.com/Acestus/template-functions-dotnet'
    }
}

# Deploy using deployment stack
Write-Host "Deploying infrastructure stack..." -ForegroundColor Cyan

$StackParams = @{
    Name                    = $StackName
    ResourceGroupName       = $ResourceGroupName
    TemplateFile            = $TemplateFile
    TemplateParameterFile   = $ParameterFile
    ActionOnUnmanage        = 'DeleteResources'
    DenySettingsMode        = 'None'
}

New-AzResourceGroupDeploymentStack @StackParams

Write-Host "Infrastructure deployment complete!" -ForegroundColor Green
