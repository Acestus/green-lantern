using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace HelloWorld;

public class ComplianceFunction
{
    private const string EnableAzureCliVariable = "AZURE_CLI_ENABLE";
    private const string AzureCliPathVariable = "AZURE_CLI_PATH";
    private const string AzureSubscriptionIdVariable = "AZURE_SUBSCRIPTION_ID";

    private readonly ILogger<ComplianceFunction> _logger;

    public ComplianceFunction(ILogger<ComplianceFunction> logger)
    {
        _logger = logger;
    }

    [Function("ComplianceDashboard")]
    public async Task<IActionResult> ComplianceDashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "compliance/dashboard")] HttpRequest req)
    {
        var correlationId = HelloFunctionHelpers.GetCorrelationId(req);
        _logger.LogInformation("Compliance dashboard requested. CID:{CorrelationId}", correlationId);

        if (!IsAzureCliEnabled())
        {
            return new OkObjectResult(CreateDemoDashboard(correlationId));
        }

        var subscription = await RunAzureCliAsync("account", "show", "--output", "json");
        if (subscription.ExitCode != 0)
        {
            return AzureCliFailure(subscription);
        }

        var resourceGroups = await RunAzureCliAsync(
            "group",
            "list",
            "--query",
            "[].{name:name,location:location,provisioningState:properties.provisioningState,tags:tags}",
            "--output",
            "json");
        if (resourceGroups.ExitCode != 0)
        {
            return AzureCliFailure(resourceGroups);
        }

        var policySummary = await RunAzureCliAsync(
            "policy",
            "state",
            "summarize",
            "--output",
            "json");

        return new OkObjectResult(new
        {
            correlationId,
            generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            mode = "azure-cli",
            azureCliEnabled = true,
            subscription = ParseJsonOrString(subscription.Stdout),
            resourceGroups = ParseJsonOrEmptyArray(resourceGroups.Stdout),
            policySummary = policySummary.ExitCode == 0
                ? ParseJsonOrString(policySummary.Stdout)
                : new
                {
                    warning = "Azure Policy summary command failed.",
                    command = policySummary.Command,
                    error = policySummary.Stderr
                },
            cliCommands = new
            {
                resourceGroups = "az group list --query \"[].{name:name,location:location,provisioningState:properties.provisioningState,tags:tags}\" -o json",
                policySummary = "az policy state summarize -o json",
                remediation = "az policy remediation create --name <name> --policy-assignment <assignment-id-or-name> --resource-group <resource-group> --resource-discovery-mode ReEvaluateCompliance -o json"
            }
        });
    }

    [Function("ComplianceRemediation")]
    public async Task<IActionResult> ComplianceRemediation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "compliance/remediation")] HttpRequest req)
    {
        var correlationId = HelloFunctionHelpers.GetCorrelationId(req);
        _logger.LogInformation("Compliance remediation requested. CID:{CorrelationId}", correlationId);

        var request = await ReadRemediationRequestAsync(req);
        if (string.IsNullOrWhiteSpace(request.ResourceGroup))
        {
            return new BadRequestObjectResult(new { error = "resourceGroup is required.", correlationId });
        }

        var remediationName = string.IsNullOrWhiteSpace(request.RemediationName)
            ? $"green-lantern-remediate-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : request.RemediationName.Trim();

        if (!IsAzureCliEnabled())
        {
            return new OkObjectResult(new
            {
                correlationId,
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                mode = "demo",
                status = "demo-queued",
                resourceGroup = request.ResourceGroup,
                remediationName,
                message = "Azure CLI execution is disabled. Set AZURE_CLI_ENABLE=true on the Function App to run this command.",
                command = BuildRemediationCommand(remediationName, request.PolicyAssignmentId ?? "<policy-assignment-id>", request.ResourceGroup)
            });
        }

        if (string.IsNullOrWhiteSpace(request.PolicyAssignmentId))
        {
            return new BadRequestObjectResult(new
            {
                error = "policyAssignmentId is required when AZURE_CLI_ENABLE=true.",
                correlationId
            });
        }

        var result = await RunAzureCliAsync(
            "policy",
            "remediation",
            "create",
            "--name",
            remediationName,
            "--policy-assignment",
            request.PolicyAssignmentId.Trim(),
            "--resource-group",
            request.ResourceGroup.Trim(),
            "--resource-discovery-mode",
            "ReEvaluateCompliance",
            "--output",
            "json");

        if (result.ExitCode != 0)
        {
            return AzureCliFailure(result);
        }

        return new OkObjectResult(new
        {
            correlationId,
            generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            mode = "azure-cli",
            status = "submitted",
            resourceGroup = request.ResourceGroup,
            remediationName,
            command = result.Command,
            result = ParseJsonOrString(result.Stdout)
        });
    }

    private static bool IsAzureCliEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(EnableAzureCliVariable),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static object CreateDemoDashboard(string correlationId)
    {
        return new
        {
            correlationId,
            generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            mode = "demo",
            azureCliEnabled = false,
            warning = "Azure CLI execution is disabled. Set AZURE_CLI_ENABLE=true and grant the Function identity Azure Reader plus Policy permissions to use live Azure Policy data.",
            subscription = new
            {
                id = Environment.GetEnvironmentVariable(AzureSubscriptionIdVariable) ?? "demo-subscription",
                name = "Green Lantern demo subscription"
            },
            resourceGroups = new[]
            {
                new { name = "rg-lantrnfx-dev", location = "westus2", provisioningState = "Succeeded", compliance = "Non-compliant", nonCompliantResources = 3 },
                new { name = "rg-lantern-dev", location = "westus2", provisioningState = "Succeeded", compliance = "Compliant", nonCompliantResources = 0 },
                new { name = "rg-shared-observability", location = "westus2", provisioningState = "Succeeded", compliance = "Review", nonCompliantResources = 1 }
            },
            policySummary = new
            {
                nonCompliantResources = 4,
                nonCompliantPolicies = 2,
                policyAssignments = 3
            },
            cliCommands = new
            {
                resourceGroups = "az group list --query \"[].{name:name,location:location,provisioningState:properties.provisioningState,tags:tags}\" -o json",
                policySummary = "az policy state summarize -o json",
                remediation = "az policy remediation create --name <name> --policy-assignment <assignment-id-or-name> --resource-group <resource-group> --resource-discovery-mode ReEvaluateCompliance -o json"
            }
        };
    }

    private static async Task<RemediationRequest> ReadRemediationRequestAsync(HttpRequest req)
    {
        if (req.ContentLength is null or 0)
        {
            return new RemediationRequest(null, null, null);
        }

        using var reader = new StreamReader(req.Body);
        var body = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return new RemediationRequest(null, null, null);
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        return new RemediationRequest(
            GetString(root, "resourceGroup"),
            GetString(root, "policyAssignmentId"),
            GetString(root, "remediationName"));
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static async Task<AzureCliResult> RunAzureCliAsync(params string[] arguments)
    {
        var executable = Environment.GetEnvironmentVariable(AzureCliPathVariable);
        if (string.IsNullOrWhiteSpace(executable))
        {
            executable = "az";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in AppendSubscription(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var command = $"{executable} {string.Join(' ', startInfo.ArgumentList.Select(QuoteForDisplay))}";

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new AzureCliResult(command, 127, string.Empty, "Failed to start Azure CLI process.");
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return new AzureCliResult(command, process.ExitCode, stdout, stderr);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new AzureCliResult(command, 127, string.Empty, ex.Message);
        }
    }

    private static string QuoteForDisplay(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static IEnumerable<string> AppendSubscription(string[] arguments)
    {
        foreach (var argument in arguments)
        {
            yield return argument;
        }

        var subscriptionId = Environment.GetEnvironmentVariable(AzureSubscriptionIdVariable);
        if (!string.IsNullOrWhiteSpace(subscriptionId) &&
            !arguments.Contains("--subscription", StringComparer.OrdinalIgnoreCase))
        {
            yield return "--subscription";
            yield return subscriptionId;
        }
    }

    private static string BuildRemediationCommand(string remediationName, string policyAssignmentId, string resourceGroup)
    {
        return string.Join(' ', new[]
        {
            "az",
            "policy",
            "remediation",
            "create",
            "--name",
            QuoteForDisplay(remediationName),
            "--policy-assignment",
            QuoteForDisplay(policyAssignmentId),
            "--resource-group",
            QuoteForDisplay(resourceGroup),
            "--resource-discovery-mode",
            "ReEvaluateCompliance",
            "-o",
            "json"
        });
    }

    private static IActionResult AzureCliFailure(AzureCliResult result)
    {
        return new ObjectResult(new
        {
            error = "Azure CLI command failed.",
            command = result.Command,
            exitCode = result.ExitCode,
            stdout = result.Stdout,
            stderr = result.Stderr
        })
        {
            StatusCode = 502
        };
    }

    private static object ParseJsonOrString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(value);
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static object ParseJsonOrEmptyArray(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<object>();
        }

        return ParseJsonOrString(value);
    }

    private sealed record RemediationRequest(string? ResourceGroup, string? PolicyAssignmentId, string? RemediationName);

    private sealed record AzureCliResult(string Command, int ExitCode, string Stdout, string Stderr);
}
