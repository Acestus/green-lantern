# Green Lantern - .NET Azure Functions Template

A template repository for .NET Azure Functions following Cloud Adoption Framework (CAF) naming conventions and Infrastructure as Code best practices.

## Quickstart (GitHub Action deploy to dev)

Use this when you want push-to-deploy from GitHub Actions.

1. Push to `main`.
2. Open **Actions** in this repository if you want to watch the run.
3. Monitor until all deploy/build/publish steps are green.

For manual runs, open **Actions**, select **Deploy to dev**
(`.github/workflows/deploy-to-dev.yaml`), and click **Run workflow**.
The default `projectName` for this repo is `lantern`.
This note exists to give the push-based dev trigger a fresh change to pick up.

The GitHub Action workflows are pinned to the `acestus` subscription
(`df64929f-810d-4176-8097-35cd05cae10d`) and use OIDC with the managed identity
`/subscriptions/df64929f-810d-4176-8097-35cd05cae10d/resourceGroups/rg-mgmt-dev/providers/Microsoft.ManagedIdentity/userAssignedIdentities/umi-mgmt-dev-scus-ctl`.
The `Deploy to dev` workflow runs in the GitHub Environment `dev`, which is
documented in [`.github/environments/dev.md`](.github/environments/dev.md).

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
green-lantern/
├── hello-world/                    # Hello World function app
│   ├── HelloWorld.csproj
│   ├── Program.cs
│   ├── HelloFunction.cs
│   ├── host.json
│   └── local.settings.json
├── tests/                          # Unit and acceptance tests
│   ├── HelloWorld.UnitTests/
│   └── HelloWorld.AcceptanceTests/
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
- `GET /api/compliance/dashboard` - Returns Azure Policy/resource group compliance dashboard data
- `POST /api/compliance/remediation` - Starts or previews an Azure Policy remediation task

### Compliance Demo

The Static Web App includes `/compliance.html`, a quick Azure Policy demo page
that shows policy summary metrics, resource groups, Azure CLI command previews,
and a remediation trigger.

By default, the Function returns demo data and does not execute Azure CLI. To
use live Azure CLI-backed data, configure the Function App with:

```bash
AZURE_CLI_ENABLE=true
AZURE_CLI_PATH=az
AZURE_SUBSCRIPTION_ID=<subscription-id>
```

The Function identity or runtime identity must be able to run Azure CLI and must
have enough Azure RBAC to read resource groups and policy state. For live
remediation, it also needs permission to create policy remediation tasks for the
target scope.

Useful commands for validating the same data locally:

```bash
az group list \
  --query "[].{name:name,location:location,provisioningState:properties.provisioningState,tags:tags}" \
  -o json

az policy state summarize -o json

az policy remediation create \
  --name green-lantern-remediate-demo \
  --policy-assignment <policy-assignment-id-or-name> \
  --resource-group <resource-group-name> \
  --resource-discovery-mode ReEvaluateCompliance \
  -o json
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azure Functions Core Tools v4](https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local)
- [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)
- [Azure PowerShell](https://docs.microsoft.com/en-us/powershell/azure/install-az-ps)
- [Hugo extended](https://gohugo.io/installation/) (for building the Static Web App)
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

### Static Web App

The Static Web App under `swa/hello-portal` is built with Hugo. Existing portal
pages live under `swa/hello-portal/static` so their published routes stay stable,
while Bun still bundles the TypeScript dashboard assets before Hugo emits the
deployable `public` directory.

```bash
cd swa/hello-portal
bun install
bun run typecheck
bun run build
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

### Tests

The `Tests` GitHub Actions workflow runs unit and acceptance tests in parallel on pushes to `main` and on pull requests.

## Naming Conventions

This template follows the Cloud Adoption Framework (CAF) naming conventions:

| Resource | Pattern | Example |
|----------|---------|---------|
| Resource Group | `rg-{cafName}` | `rg-functemplate-prd-usw2-001` |
| Function App | `func-{project}-{purpose}-{env}-{region}-{instance}` | `func-functemplate-hello-prd-usw2-001` |
| Storage Account | `st{project}{env}{region}{instance}` | `stfunctemplprdusw2001` |
| Deployment Stack | `stack-{cafName}` | `stack-functemplate-prd-usw2-001` |

## CI/CD

### CI quality gate

The `CI` workflow (`.github/workflows/ci.yaml`) runs on pull requests, pushes to
`main`, and manual dispatch. It validates the deployable pieces before the
environment workflows publish anything:

1. Compiles `infrastructure/main.bicep`
2. Restores, builds, tests, and publishes the Azure Functions app
3. Typechecks the Static Web App's vanilla TypeScript sources
4. Builds the browser assets that land in `swa/hello-portal/assets/`
5. Uploads compiled Bicep, Function, test result, and SWA artifacts

### One-click bootstrap (copy repo and run)

Use one of the two manual button workflows:
- `.github/workflows/deploy-to-dev.yaml`
- `.github/workflows/deploy-to-prd.yaml`

The dev workflow now also runs automatically on pushes to `main`.

Both workflows accept:
- `projectName` for manual runs, defaulting to `lantern` in dev

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
