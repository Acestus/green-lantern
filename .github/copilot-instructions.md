# AI Coding Guidelines for Azure Functions Template

This document provides guidelines for AI assistants working with this repository.

## Repository Overview

This is a .NET Azure Functions template repository with one function app:
- **hello-world**: Simple HTTP trigger greeting function

## Project Conventions

### Directory Structure
- Each function app has its own directory with a `.csproj`, `Program.cs`, function classes, and `host.json`
- Infrastructure as Code lives in `infrastructure/`
- Deployment scripts live in `scripts/`
- GitHub workflows live in `.github/workflows/`

### Naming Conventions
- **CAF Naming**: Follow Cloud Adoption Framework naming: `{projectName}-{environment}-{region}-{instanceNumber}`
- **Function Apps**: `func-{project}-{purpose}-{env}-{region}-{instance}` (e.g., `func-functemplate-hello-prd-usw2-001`)
- **Storage Accounts**: `st{project}{env}{region}{instance}` (lowercase, no hyphens)
- **Resource Groups**: `rg-{cafName}` (e.g., `rg-functemplate-prd-usw2-001`)
- **Deployment Stacks**: `stack-{cafName}` (e.g., `stack-functemplate-prd-usw2-001`)

### Tags
All Azure resources should include these tags:
```bicep
tags = {
  ManagedBy: 'https://github.com/Acestus/template-functions-dotnet'
  CreatedBy: '{username}'
  Environment: '{Development|Production}'
  Subscription: '{subscription-name}'
  Project: '{project-description}'
  CAFName: '{cafName}'
}
```

## Bicep Guidelines

### Module Sources
Use custom modules from the Skopos ACR:
```bicep
// Storage Account
module storageAccount 'br:acrskpmgtcrdevusw2001.azurecr.io/bicep/modules/storage-account:v1.0.2' = { ... }

// Function App
module functionApp 'br:acrskpmgtcrdevusw2001.azurecr.io/bicep/modules/function-app:v1.0.0' = { ... }
```

### Style Guide
- One directory per resource group
- One workflow YAML per resource group
- Deploy with `az stack group create` (deployment stacks) - not `az deployment group create`
- Use Azure Verified Modules (AVM) when custom modules are unavailable
- Self-contained directories - duplicate values instead of cross-referencing
- Use `.bicepparam` files for environment-specific parameters

### Parameter Files
- Use `main.dev.bicepparam` for development
- Use `main.prd.bicepparam` for production
- Always use `using 'main.bicep'` directive

## .NET Function App Guidelines

### Version
- Use .NET 8.0 (LTS) for function apps
- Use Azure Functions v4 with isolated worker model

### Project Structure
```
function-app/
├── FunctionApp.csproj
├── Program.cs
├── FunctionClass.cs
├── host.json
└── local.settings.json
```

### Dependencies
Use these NuGet packages:
```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker" Version="1.21.0" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http" Version="3.1.0" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore" Version="1.2.1" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="1.17.2" />
<PackageReference Include="Microsoft.ApplicationInsights.WorkerService" Version="2.22.0" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.ApplicationInsights" Version="1.2.0" />
```

### Code Style
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace MyFunctionApp;

public class MyFunction
{
    private readonly ILogger<MyFunction> _logger;

    public MyFunction(ILogger<MyFunction> logger)
    {
        _logger = logger;
    }

    [Function("MyFunction")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "myroute")] HttpRequest req)
    {
        _logger.LogInformation("Processing request.");
        return new OkObjectResult(new { message = "Hello!" });
    }
}
```

### Program.cs (Isolated Worker)
```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
    })
    .Build();

host.Run();
```

### host.json
Use standard configuration:
```json
{
  "version": "2.0",
  "logging": {
    "applicationInsights": {
      "samplingSettings": {
        "isEnabled": true,
        "excludedTypes": "Request"
      }
    }
  }
}
```

## Deployment Guidelines

### Infrastructure Deployment
Use Azure Deployment Stacks:
```powershell
$StackParams = @{
    Name              = 'stack-{cafName}'
    ResourceGroupName = 'rg-{cafName}'
    TemplateFile      = 'main.bicep'
    TemplateParameterFile = 'main.{env}.bicepparam'
    ActionOnUnmanage = 'DeleteResources'
    DenySettingsMode = 'None'
}
New-AzResourceGroupDeploymentStack @StackParams
```

### Code Deployment
Build and deploy with Azure Functions Core Tools:
```powershell
# Build
dotnet build --configuration Release
dotnet publish --configuration Release --output ./publish

# Deploy
cd ./publish
func azure functionapp publish {function-app-name}
```

### Parallel Deployments
For multiple function apps, use PowerShell jobs:
```powershell
$jobs = @(
    @{ Name = "func-name-1"; Dir = "function-dir-1" }
    @{ Name = "func-name-2"; Dir = "function-dir-2" }
)
$runningJobs = $jobs | ForEach-Object {
    Start-Job -ScriptBlock { ... }
}
$runningJobs | Wait-Job | Receive-Job
```

## GitHub Actions

### Workflow Structure
- Trigger on push to main branch
- Use OIDC authentication with Azure (federated credentials)
- Deploy infrastructure first, then code
- Deploy function apps in parallel

### Required Secrets/Variables
- `AZURE_CLIENT_ID`: Client ID for the `umi-mgmt-dev-scus-ctl` managed identity
- `AZURE_TENANT_ID`: Azure AD tenant ID
- Target subscription: `acestus` (`df64929f-810d-4176-8097-35cd05cae10d`)
- `RESOURCE_GROUP_NAME`: Target resource group

## Code Review Conventions

Use [Conventional Comments](https://conventionalcomments.org/):
- `issue (blocking): {description}` - Must be addressed
- `issue (non-blocking): {description}` - Should be addressed
- `suggestion: {description}` - Optional improvement
- `nitpick: {description}` - Minor style issue

## Security Best Practices

- Use Managed Identity for Azure service authentication
- Store secrets in Azure Key Vault with `@Microsoft.KeyVault()` references
- Enable HTTPS only on function apps
- Use private endpoints where appropriate
- Enable Application Insights for monitoring

## Testing

### Local Development
```bash
cd {function-app-directory}
dotnet build
func start
```

### Running Tests
```bash
dotnet test
```
