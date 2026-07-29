<#
.SYNOPSIS
    Deploys Azure Functions code to Azure.

.DESCRIPTION
    This script builds and deploys the .NET function apps to Azure using
    Azure Functions Core Tools.

.PARAMETER Environment
    Target environment (dev or prd)

.EXAMPLE
    ./deploy-code.ps1 -Environment dev
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

$CurrentSubscriptionId = (az account show --query id -o tsv 2>$null).Trim()
if ([string]::IsNullOrWhiteSpace($CurrentSubscriptionId)) {
    throw "No Azure CLI context found. Sign in first, then rerun this script."
}

if ($CurrentSubscriptionId -ne $TargetSubscriptionId) {
    throw "This template only deploys to subscription $TargetSubscriptionId (acestus). Current subscription: $CurrentSubscriptionId"
}

# Function app to deploy
$FunctionApps = @(
    @{
        Name      = "func-$ProjectName-hello-$Environment-$Region-$InstanceNumber"
        Directory = 'hello-world'
    }
)

Write-Host "Deploying function apps for environment: $Environment" -ForegroundColor Cyan

# Deploy each function app in parallel
$Jobs = $FunctionApps | ForEach-Object {
    $app = $_
    Start-Job -ScriptBlock {
        param($AppName, $AppDir, $RootPath)
        
        $FullPath = Join-Path $RootPath $AppDir
        Set-Location $FullPath
        
        Write-Output "Building $AppName..."
        dotnet build --configuration Release
        
        Write-Output "Publishing $AppName..."
        dotnet publish --configuration Release --output ./publish
        
        Set-Location ./publish
        
        Write-Output "Deploying $AppName..."
        func azure functionapp publish $AppName
        
        Write-Output "Deployed $AppName successfully!"
    } -ArgumentList $app.Name, $app.Directory, $PSScriptRoot/..
}

# Wait for all deployments to complete
$Jobs | Wait-Job | Receive-Job

# Clean up jobs
$Jobs | Remove-Job

Write-Host "Function app deployed successfully!" -ForegroundColor Green
