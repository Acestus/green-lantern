# Azure Functions Template - .NET

A template repository for .NET Azure Functions following Cloud Adoption Framework (CAF) naming conventions and Infrastructure as Code best practices.

## Quickstart (GitHub Action deploy to dev)

Use this when you want one-click deployment from GitHub Actions.

1. Open **Actions** in this repository.
2. Select **Deploy to dev** (`.github/workflows/deploy-to-dev.yaml`).
3. Click **Run workflow**.
4. Set:
   - `projectName` (example: `hello02`)
5. Run and monitor until all deploy/build/publish steps are green.

The GitHub Action workflows are pinned to the `acestus` subscription
(`df64929f-810d-4176-8097-35cd05cae10d`) and use OIDC with the managed identity
`/subscriptions/df64929f-810d-4176-8097-35cd05cae10d/resourceGroups/rg-mgmt-dev/providers/Microsoft.ManagedIdentity/userAssignedIdentities/umi-mgmt-dev-scus-ctl`.

## Quickstart (Contributor role)

If you have **Contributor** access on the target subscription/resource group, you can deploy this template directly with `az`, `bicep`, and `func`:

```bash
# 1) Set deployment values
SUBSCRIPTION_ID="df64929f-810d-4176-8097-35cd05cae10d"
PROJECT_NAME="hello02"
ENVIRONMENT="dev"
REGION="usw2"
INSTANCE="001"
RG_NAME="rg-${PROJECT_NAME}-${ENVIRONMENT}"
FUNC_APP_NAME="func-${PROJECT_NAME}-hello-${ENVIRONMENT}-${REGION}-${INSTANCE}"

# 2) Azure login + subscription
az login
az account set --subscription "$SUBSCRIPTION_ID"

# 3) Create resource group
az group create --name "$RG_NAME" --location westus2

# 4) Generate bicep parameters
cat > infrastructure/main.${ENVIRONMENT}.generated.bicepparam <<EOF
using './main.bicep'
param projectName = '${PROJECT_NAME}'
param environment = '${ENVIRONMENT}'
param region = '${REGION}'
param instanceNumber = '${INSTANCE}'
param tags = {
  ManagedBy: 'manual-quickstart'
  CreatedBy: 'local-user'
  Environment: 'Development'
  Subscription: '${SUBSCRIPTION_ID}'
  Project: 'Azure Functions Template'
  CAFName: '${PROJECT_NAME}-${ENVIRONMENT}-${REGION}-${INSTANCE}'
}
EOF

# 5) Deploy infrastructure
az deployment group create \
  --resource-group "$RG_NAME" \
  --template-file infrastructure/main.bicep \
  --parameters "infrastructure/main.${ENVIRONMENT}.generated.bicepparam"

# 6) Publish function code
cd hello-world
dotnet publish -c Release -o publish
func azure functionapp publish "$FUNC_APP_NAME" --dotnet-isolated
cd ..
```

### Required access

At minimum, your identity needs:
- **Contributor** on the target resource group or subscription
- Permission to deploy `Microsoft.Web/*`, `Microsoft.Storage/*`, `Microsoft.ManagedIdentity/*`, and `Microsoft.Insights/*` resources

## Repository Structure

```
template-functions-dotnet/
├── hello-world/                    # Hello World function app
│   ├── HelloWorld.csproj
│   ├── Program.cs
│   ├── HelloFunction.cs
│   ├── host.json
│   └── local.settings.json
├── infrastructure/                 # Bicep IaC templates
│   ├── main.bicep
│   ├── main.dev.bicepparam
│   └── main.prd.bicepparam
├── scripts/                        # Deployment scripts
│   ├── deploy-infra.ps1
│   └── deploy-code.ps1
└── .github/
    ├── workflows/
    │   ├── deploy-to-dev.yaml
    │   └── deploy-to-prd.yaml
    ├── instructions/
    │   └── pr-review.instructions.md
    └── copilot-instructions.md
```

## Function Apps

### Hello World
HTTP trigger app that provides greeting plus live storage and observability APIs.

**Endpoints:**
- `GET /api/health` - Returns service health/status with current UTC timestamp
- `GET /api/hello` - Returns "Hello, World!"
- `GET /api/hello?name=John` - Returns "Hello, John!"
- `POST /api/hello` with body `{"name": "John"}` - Returns "Hello, John!"
- `GET /api/storage/summary` - Returns live Blob/Queue/Table data from the deployed storage account
- `GET /api/observability/appinsights` - Returns live App Insights request/error/latency data (last hour)

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azure Functions Core Tools v4](https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local)
- [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)
- [Azure PowerShell](https://docs.microsoft.com/en-us/powershell/azure/install-az-ps)
- [Bun](https://bun.sh/) (for bundling SWA Chart.js assets)

## Local Development

1. Navigate to a function app directory:
   ```bash
   cd hello-world
   ```

2. Build the project:
   ```bash
   dotnet build
   ```

3. Start the function app:
   ```bash
   func start
   ```

## Deployment

### Infrastructure

Deploy infrastructure using Azure Deployment Stacks:

```powershell
./scripts/deploy-infra.ps1 -Environment dev
```

### Code

Deploy function apps:

```powershell
./scripts/deploy-code.ps1 -Environment dev
```

## Naming Conventions

This template follows the Cloud Adoption Framework (CAF) naming conventions:

| Resource | Pattern | Example |
|----------|---------|---------|
| Resource Group | `rg-{cafName}` | `rg-functemplate-prd-usw2-001` |
| Function App | `func-{project}-{purpose}-{env}-{region}-{instance}` | `func-functemplate-hello-prd-usw2-001` |
| Storage Account | `st{project}{env}{region}{instance}` | `stfunctemplprdusw2001` |
| Deployment Stack | `stack-{cafName}` | `stack-functemplate-prd-usw2-001` |

## CI/CD

### One-click bootstrap (copy repo and run)

Use one of the two manual button workflows:
- `.github/workflows/deploy-to-dev.yaml`
- `.github/workflows/deploy-to-prd.yaml`

Both accept:
- `projectName`

Both workflows deploy only to the `acestus` subscription
(`df64929f-810d-4176-8097-35cd05cae10d`) and authenticate with OIDC through the
managed identity `/subscriptions/df64929f-810d-4176-8097-35cd05cae10d/resourceGroups/rg-mgmt-dev/providers/Microsoft.ManagedIdentity/userAssignedIdentities/umi-mgmt-dev-scus-ctl`.

That workflow deterministically deploys:
1. Resource group (`rg-{projectName}-dev`)
2. Storage account
3. One data-plane UMI (`dat-001`)
4. One Function App (hello-world)
5. Static Web App
6. Storage-hosted HTML reports uploaded from `storage-reports/` (Architecture + KPI samples)
7. Parallel app deployment after infra (`deploy-function` and `deploy-swa` jobs run at the same time)
8. Static Web App pages from `swa/hello-portal/`
9. Function code publish for hello-world (including health/report APIs)
10. Smoke tests for Function health and SWA page readiness before marking success
11. Deployment summary with resource inventory, SWA URL, and health curl command

Both workflows are deterministic and environment-fixed:
- **Deploy to dev** creates `rg-{projectName}-dev` and deploys/publishes with `env=dev`
- **Deploy to prd** creates `rg-{projectName}-prd` and deploys/publishes with `env=prd`

### Required Secrets

No GitHub variables are required for OIDC in this template. The workflows use
the managed identity client ID and tenant ID baked into the workflow files.

## Technologies

- **Runtime**: .NET 8.0 (LTS)
- **Model**: Azure Functions v4 Isolated Worker
- **IaC**: Bicep with Azure Deployment Stacks
- **CI/CD**: GitHub Actions with OIDC authentication
