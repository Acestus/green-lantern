using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HelloWorld.AcceptanceTests;

[CollectionDefinition("Acceptance", DisableParallelization = true)]
public sealed class AcceptanceCollectionDefinition { }

[Collection("Acceptance")]
public class HelloFunctionAcceptanceTests
{
    private readonly HelloFunction _sut = new(NullLogger<HelloFunction>.Instance);
    private readonly ComplianceFunction _compliance = new(NullLogger<ComplianceFunction>.Instance);

    [Fact]
    public async Task Hello_returns_a_personalized_message_from_the_query_string()
    {
        var request = CreateRequest("GET", queryString: "?name=Lantern");

        var result = await _sut.Run(request);

        var payload = ReadJson(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("Hello, Lantern!", payload.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Hello_returns_a_personalized_message_from_the_request_body()
    {
        var request = CreateRequest("POST", body: "{\"name\":\"Dev\"}");

        var result = await _sut.Run(request);

        var payload = ReadJson(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("Hello, Dev!", payload.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void Health_returns_ok_and_uses_the_configured_site_name()
    {
        var original = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
        Environment.SetEnvironmentVariable("WEBSITE_SITE_NAME", "lantern-dev");

        try
        {
            var result = _sut.Health(CreateRequest());

            var payload = ReadJson(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal("ok", payload.RootElement.GetProperty("status").GetString());
            Assert.Equal("lantern-dev", payload.RootElement.GetProperty("service").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("WEBSITE_SITE_NAME", original);
        }
    }

    [Fact]
    public async Task StorageSummary_returns_a_configuration_error_when_storage_is_missing()
    {
        var original = Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING");
        Environment.SetEnvironmentVariable("STORAGE_CONNECTION_STRING", null);

        try
        {
            var result = await _sut.StorageSummary(CreateRequest());

            var payload = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, payload.StatusCode);

            var json = ReadJson(payload.Value);
            Assert.Equal("STORAGE_CONNECTION_STRING is not configured.", json.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("STORAGE_CONNECTION_STRING", original);
        }
    }

    [Fact]
    public async Task AppInsightsSummary_returns_a_warning_when_app_insights_is_missing()
    {
        var originalAppId = Environment.GetEnvironmentVariable("APPINSIGHTS_APP_ID");
        var originalApiKey = Environment.GetEnvironmentVariable("APPINSIGHTS_API_KEY");
        Environment.SetEnvironmentVariable("APPINSIGHTS_APP_ID", null);
        Environment.SetEnvironmentVariable("APPINSIGHTS_API_KEY", null);

        try
        {
            var result = await _sut.AppInsightsSummary(CreateRequest());

            var payload = ReadJson(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal("APPINSIGHTS_APP_ID or APPINSIGHTS_API_KEY is not configured.", payload.RootElement.GetProperty("warning").GetString());
            Assert.Empty(payload.RootElement.GetProperty("points").EnumerateArray());
        }
        finally
        {
            Environment.SetEnvironmentVariable("APPINSIGHTS_APP_ID", originalAppId);
            Environment.SetEnvironmentVariable("APPINSIGHTS_API_KEY", originalApiKey);
        }
    }

    [Fact]
    public async Task StorageReportLinks_returns_a_configuration_error_when_storage_is_missing()
    {
        var original = Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING");
        Environment.SetEnvironmentVariable("STORAGE_CONNECTION_STRING", null);

        try
        {
            var result = await _sut.StorageReportLinks(CreateRequest());

            var payload = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, payload.StatusCode);

            var json = ReadJson(payload.Value);
            Assert.Equal("STORAGE_CONNECTION_STRING is not configured.", json.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("STORAGE_CONNECTION_STRING", original);
        }
    }

    [Fact]
    public async Task ComplianceDashboard_returns_demo_data_when_azure_cli_is_disabled()
    {
        var original = Environment.GetEnvironmentVariable("AZURE_CLI_ENABLE");
        Environment.SetEnvironmentVariable("AZURE_CLI_ENABLE", null);

        try
        {
            var result = await _compliance.ComplianceDashboard(CreateRequest());

            var payload = ReadJson(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal("demo", payload.RootElement.GetProperty("mode").GetString());
            Assert.False(payload.RootElement.GetProperty("azureCliEnabled").GetBoolean());
            Assert.True(payload.RootElement.GetProperty("resourceGroups").GetArrayLength() >= 1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZURE_CLI_ENABLE", original);
        }
    }

    [Fact]
    public async Task ComplianceRemediation_returns_demo_queue_response_when_azure_cli_is_disabled()
    {
        var original = Environment.GetEnvironmentVariable("AZURE_CLI_ENABLE");
        Environment.SetEnvironmentVariable("AZURE_CLI_ENABLE", null);

        try
        {
            var result = await _compliance.ComplianceRemediation(CreateRequest(
                "POST",
                body: "{\"resourceGroup\":\"rg-lantrnfx-dev\",\"policyAssignmentId\":\"demo-assignment\"}"));

            var payload = ReadJson(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal("demo-queued", payload.RootElement.GetProperty("status").GetString());
            Assert.Contains("az policy remediation create", payload.RootElement.GetProperty("command").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZURE_CLI_ENABLE", original);
        }
    }

    private static HttpRequest CreateRequest(string method = "GET", string? queryString = null, string? body = null)
    {
        var context = new DefaultHttpContext();
        var request = context.Request;
        request.Method = method;
        if (!string.IsNullOrWhiteSpace(queryString))
        {
            request.QueryString = new QueryString(queryString);
        }

        if (body is not null)
        {
            var buffer = Encoding.UTF8.GetBytes(body);
            request.Body = new MemoryStream(buffer);
            request.ContentLength = buffer.Length;
        }

        return request;
    }

    private static JsonDocument ReadJson(object? value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonDocument.Parse(json);
    }
}
