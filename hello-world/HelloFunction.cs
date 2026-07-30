using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HelloWorld;

public class HelloFunction
{
    private readonly ILogger<HelloFunction> _logger;
    private static readonly HttpClient HttpClient = new();

    public HelloFunction(ILogger<HelloFunction> logger)
    {
        _logger = logger;
    }

    [Function("Hello")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "hello")] HttpRequest req)
    {
        var correlationId = HelloFunctionHelpers.GetCorrelationId(req);
        _logger.LogInformation("Hello function processed a request. CID:{CorrelationId}", correlationId);

        string? name = req.Query["name"];

        if (string.IsNullOrEmpty(name) && req.ContentLength > 0)
        {
            try
            {
                using var reader = new StreamReader(req.Body);
                var body = await reader.ReadToEndAsync();
                var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("name", out var nameElement))
                {
                    name = nameElement.GetString();
                }
            }
            catch (JsonException)
            {
                // Ignore JSON parsing errors
            }
        }

        name ??= "World";

        return new OkObjectResult(new
        {
            message = $"Hello, {name}!",
            correlationId
        });
    }

    [Function("Health")]
    public IActionResult Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req)
    {
        var correlationId = HelloFunctionHelpers.GetCorrelationId(req);
        return new OkObjectResult(new
        {
            status = "ok",
            service = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "hello-world-function",
            timestampUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            correlationId
        });
    }

    [Function("StorageSummary")]
    public async Task<IActionResult> StorageSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "storage/summary")] HttpRequest req)
    {
        var correlationId = HelloFunctionHelpers.GetCorrelationId(req);
        _logger.LogInformation("Storage summary requested. CID:{CorrelationId}", correlationId);

        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new ObjectResult(new { error = "AzureWebJobsStorage is not configured." }) { StatusCode = 500 };
        }

        var containerClient = new BlobContainerClient(connectionString, "samples");
        await containerClient.CreateIfNotExistsAsync();

        var sampleBlobName = $"hello-{DateTime.UtcNow:yyyyMMdd}.txt";
        var sampleBlob = containerClient.GetBlobClient(sampleBlobName);
        if (!await sampleBlob.ExistsAsync())
        {
            await sampleBlob.UploadAsync(BinaryData.FromString($"hello sample generated at {DateTime.UtcNow:O}"));
        }

        var blobCount = 0;
        var blobNames = new List<string>();
        DateTimeOffset? latestBlobLastModified = null;
        await foreach (var blob in containerClient.GetBlobsAsync())
        {
            blobCount++;
            if (blobNames.Count < 5)
            {
                blobNames.Add(blob.Name);
            }
            if (!latestBlobLastModified.HasValue || blob.Properties.LastModified > latestBlobLastModified)
            {
                latestBlobLastModified = blob.Properties.LastModified;
            }
        }

        var queueClient = new QueueClient(connectionString, "hello-requests");
        await queueClient.CreateIfNotExistsAsync();
        await queueClient.SendMessageAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes($"hello request at {DateTime.UtcNow:O}")));
        var queueProperties = await queueClient.GetPropertiesAsync();
        var peekedMessages = await queueClient.PeekMessagesAsync(maxMessages: 1);
        PeekedMessage? latestQueueMessage = peekedMessages.Value?.FirstOrDefault();

        var tableClient = new TableClient(connectionString, "HelloAudit");
        await tableClient.CreateIfNotExistsAsync();

        var rowKey = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        await tableClient.UpsertEntityAsync(new TableEntity("hello", rowKey)
        {
            ["Source"] = "storage-summary",
            ["TimestampUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        });

        var auditRows = new List<object>();
        DateTimeOffset? latestTableTimestamp = null;
        await foreach (var entity in tableClient.QueryAsync<TableEntity>(x => x.PartitionKey == "hello", maxPerPage: 10))
        {
            if (!latestTableTimestamp.HasValue || entity.Timestamp > latestTableTimestamp)
            {
                latestTableTimestamp = entity.Timestamp;
            }
            auditRows.Add(new
            {
                partitionKey = entity.PartitionKey,
                rowKey = entity.RowKey,
                source = entity.GetString("Source"),
                timestampUtc = entity.GetString("TimestampUtc"),
                entityTimestampUtc = entity.Timestamp?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
            });
            if (auditRows.Count >= 5)
            {
                break;
            }
        }

        return new OkObjectResult(new
        {
            correlationId,
            generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            blob = new
            {
                container = containerClient.Name,
                count = blobCount,
                sample = blobNames,
                latestLastModifiedUtc = latestBlobLastModified?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
            },
            queue = new
            {
                name = queueClient.Name,
                approximateMessages = queueProperties.Value.ApproximateMessagesCount,
                latestMessageInsertedUtc = latestQueueMessage?.InsertedOn?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
            },
            table = new
            {
                name = tableClient.Name,
                latest = auditRows,
                latestEntityTimestampUtc = latestTableTimestamp?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
            },
            sourceTimestamps = new
            {
                blobLastModifiedUtc = latestBlobLastModified?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                queueLastInsertedUtc = latestQueueMessage?.InsertedOn?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                tableLastEntityTimestampUtc = latestTableTimestamp?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
            }
        });
    }

    [Function("AppInsightsSummary")]
    public async Task<IActionResult> AppInsightsSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "observability/appinsights")] HttpRequest req)
    {
        var correlationId = HelloFunctionHelpers.GetCorrelationId(req);
        _logger.LogInformation("App Insights summary requested. CID:{CorrelationId}", correlationId);

        var appId = Environment.GetEnvironmentVariable("APPINSIGHTS_APP_ID");
        var apiKey = Environment.GetEnvironmentVariable("APPINSIGHTS_API_KEY");
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(apiKey))
        {
            return new OkObjectResult(new
            {
                correlationId,
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                latestSourceTimestampUtc = (string?)null,
                warning = "APPINSIGHTS_APP_ID or APPINSIGHTS_API_KEY is not configured.",
                points = Array.Empty<object>()
            });
        }

        var query = @"
requests
| where timestamp > ago(30m)
| summarize requests=count(), errors=countif(success == false), avgDurationMs=avg(duration), p95DurationMs=percentile(duration, 95) by bin(timestamp, 5m)
| order by timestamp asc";

        try
        {
            var points = await QueryAppInsightsRowsAsync(appId, apiKey, query);
            if (points.Count == 0)
            {
                return new OkObjectResult(new { generatedAtUtc = DateTime.UtcNow.ToString("O"), points = Array.Empty<object>() });
            }

            string? latestSourceTimestampUtc = null;
            var normalizedPoints = new List<object>();
            foreach (var row in points)
            {
                var rowTimestamp = row["timestamp"]?.ToString();
                latestSourceTimestampUtc = rowTimestamp;
                normalizedPoints.Add(new
                {
                    timestamp = rowTimestamp,
                    requests = HelloFunctionHelpers.ToDouble(row["requests"]),
                    errors = HelloFunctionHelpers.ToDouble(row["errors"]),
                    avgDurationMs = HelloFunctionHelpers.ToDouble(row["avgDurationMs"]),
                    p95DurationMs = HelloFunctionHelpers.ToDouble(row["p95DurationMs"])
                });
            }

            return new OkObjectResult(new
            {
                correlationId,
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                latestSourceTimestampUtc,
                points = normalizedPoints
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "App Insights query failed.");
            return new OkObjectResult(new
            {
                correlationId,
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                warning = "Failed to query App Insights data.",
                detail = ex.Message,
                latestSourceTimestampUtc = (string?)null,
                points = Array.Empty<object>()
            });
        }
    }

    [Function("StorageReportLinks")]
    public async Task<IActionResult> StorageReportLinks(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "storage/reports-links")] HttpRequest req)
    {
        var correlationId = HelloFunctionHelpers.GetCorrelationId(req);
        _logger.LogInformation("Storage report links requested. CID:{CorrelationId}", correlationId);

        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new ObjectResult(new { error = "AzureWebJobsStorage is not configured." }) { StatusCode = 500 };
        }

        var containerClient = new BlobContainerClient(connectionString, "reports");
        await containerClient.CreateIfNotExistsAsync();

        var expiresOn = DateTimeOffset.UtcNow.AddHours(6);
        var links = new List<(string Name, string Title, string Url, string? LastModifiedUtc)>();
        await foreach (var blob in containerClient.GetBlobsAsync())
        {
            if (!blob.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var blobClient = containerClient.GetBlobClient(blob.Name);
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerClient.Name,
                BlobName = blob.Name,
                Resource = "b",
                StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
                ExpiresOn = expiresOn
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);
            var sasUri = blobClient.GenerateSasUri(sasBuilder);

            links.Add((
                blob.Name,
                HelloFunctionHelpers.ToReportTitle(blob.Name),
                sasUri.ToString(),
                blob.Properties.LastModified?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
            ));
        }

        var ordered = links
            .OrderBy(x => x.Title)
            .Select(x => new
            {
                name = x.Name,
                title = x.Title,
                url = x.Url,
                lastModifiedUtc = x.LastModifiedUtc
            })
            .ToList();

        return new OkObjectResult(new
        {
            correlationId,
            generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            expiresAtUtc = expiresOn.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            reports = ordered
        });
    }

    [Function("ObservabilityDetails")]
    public async Task<IActionResult> ObservabilityDetails(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "observability/details")] HttpRequest req)
    {
        var correlationId = HelloFunctionHelpers.GetCorrelationId(req);
        _logger.LogInformation("Observability details requested. CID:{CorrelationId}", correlationId);

        var appId = Environment.GetEnvironmentVariable("APPINSIGHTS_APP_ID");
        var apiKey = Environment.GetEnvironmentVariable("APPINSIGHTS_API_KEY");
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(apiKey))
        {
            return new OkObjectResult(new
            {
                correlationId,
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                warning = "APPINSIGHTS_APP_ID or APPINSIGHTS_API_KEY is not configured.",
                function = new
                {
                    executions = Array.Empty<object>(),
                    traces = Array.Empty<object>()
                },
                swa = new
                {
                    executions = Array.Empty<object>(),
                    traces = Array.Empty<object>()
                },
                correlations = Array.Empty<object>()
            });
        }

        var functionExecutionsQuery = @"
requests
| where timestamp > ago(30m)
| where url contains ""azurewebsites.net/api""
| project timestamp, name, resultCode, success, durationMs=todouble(duration), operation_Id
| order by timestamp desc
| take 25";

        var functionTracesQuery = @"
traces
| where timestamp > ago(30m)
| where cloud_RoleName contains ""func-""
| project timestamp, severityLevel, message, operation_Id
| order by timestamp desc
| take 25";

        var swaExecutionsQuery = @"
pageViews
| where timestamp > ago(30m)
| where tostring(customDimensions.surface) == ""swa""
| project timestamp, name, url, operation_Id, session_Id
| order by timestamp desc
| take 25";

        var swaTracesQuery = @"
traces
| where timestamp > ago(30m)
| where tostring(customDimensions.surface) == ""swa""
| project timestamp, severityLevel, message, operation_Id
| order by timestamp desc
| take 25";

        var correlationsQuery = @"
let swa = customEvents
| where timestamp > ago(30m)
| where name == ""swa.api.call""
| extend correlationId=tostring(customDimensions.correlationId)
| where isnotempty(correlationId)
| project swaTimestamp=timestamp, correlationId, page=tostring(customDimensions.page), apiPath=tostring(customDimensions.apiPath), swaOperationId=operation_Id;
let func = traces
| where timestamp > ago(30m)
| where message contains ""CID:""
| extend correlationId=extract(@""CID:([A-Za-z0-9-]+)"", 1, message)
| where isnotempty(correlationId)
| project functionTimestamp=timestamp, correlationId, functionMessage=message, functionOperationId=operation_Id;
swa
| join kind=inner func on correlationId
| project correlationId, swaTimestamp, functionTimestamp, page, apiPath, functionMessage, swaOperationId, functionOperationId
| order by functionTimestamp desc
| take 25";

        try
        {
            var functionExecutions = await QueryAppInsightsRowsAsync(appId, apiKey, functionExecutionsQuery);
            var functionTraces = await QueryAppInsightsRowsAsync(appId, apiKey, functionTracesQuery);
            var swaExecutions = await QueryAppInsightsRowsAsync(appId, apiKey, swaExecutionsQuery);
            var swaTraces = await QueryAppInsightsRowsAsync(appId, apiKey, swaTracesQuery);
            var correlations = await QueryAppInsightsRowsAsync(appId, apiKey, correlationsQuery);

            return new OkObjectResult(new
            {
                correlationId,
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                function = new
                {
                    executions = functionExecutions,
                    traces = functionTraces
                },
                swa = new
                {
                    executions = swaExecutions,
                    traces = swaTraces
                },
                correlations
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Observability details query failed.");
            return new OkObjectResult(new
            {
                correlationId,
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                warning = "Failed to query observability details.",
                detail = ex.Message,
                function = new
                {
                    executions = Array.Empty<object>(),
                    traces = Array.Empty<object>()
                },
                swa = new
                {
                    executions = Array.Empty<object>(),
                    traces = Array.Empty<object>()
                },
                correlations = Array.Empty<object>()
            });
        }
    }

    private static async Task<List<Dictionary<string, object?>>> QueryAppInsightsRowsAsync(string appId, string apiKey, string query)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.applicationinsights.io/v1/apps/{Uri.EscapeDataString(appId)}/query?query={Uri.EscapeDataString(query)}");
        request.Headers.Add("x-api-key", apiKey);

        using var response = await HttpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var failedContent = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"App Insights API query failed ({(int)response.StatusCode}): {failedContent}");
        }

        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("tables", out var tables) || tables.GetArrayLength() == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        var table = tables[0];
        var columns = table.GetProperty("columns").EnumerateArray().Select(x => x.GetProperty("name").GetString() ?? string.Empty).ToList();
        var rows = table.GetProperty("rows");
        var result = new List<Dictionary<string, object?>>();

        foreach (var row in rows.EnumerateArray())
        {
            var item = new Dictionary<string, object?>();
            for (var i = 0; i < columns.Count && i < row.GetArrayLength(); i++)
            {
                item[columns[i]] = row[i].ToString();
            }
            result.Add(item);
        }

        return result;
    }

}
