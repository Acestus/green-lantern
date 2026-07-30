using Microsoft.AspNetCore.Http;
using Xunit;

namespace HelloWorld.UnitTests;

public class HelloFunctionHelpersTests
{
    [Fact]
    public void GetCorrelationId_uses_request_header_when_present()
    {
        var request = CreateRequest(headers: new Dictionary<string, string>
        {
            ["x-correlation-id"] = "corr-123"
        });

        var correlationId = HelloFunctionHelpers.GetCorrelationId(request);

        Assert.Equal("corr-123", correlationId);
    }

    [Fact]
    public void GetCorrelationId_generates_a_guid_when_header_is_missing()
    {
        var request = CreateRequest();

        var correlationId = HelloFunctionHelpers.GetCorrelationId(request);

        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Theory]
    [InlineData("storage-report-links.html", "Storage Report Links")]
    [InlineData("executive-kpi-report.html", "Executive Kpi Report")]
    [InlineData("observability_details", "Observability Details")]
    public void ToReportTitle_normalizes_blob_names(string blobName, string expectedTitle)
    {
        var title = HelloFunctionHelpers.ToReportTitle(blobName);

        Assert.Equal(expectedTitle, title);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("12.5", 12.5)]
    [InlineData(4, 4)]
    [InlineData("not-a-number", 0)]
    public void ToDouble_converts_known_inputs(object? value, double expected)
    {
        var result = HelloFunctionHelpers.ToDouble(value);

        Assert.Equal(expected, result);
    }

    private static HttpRequest CreateRequest(Dictionary<string, string>? headers = null)
    {
        var context = new DefaultHttpContext();
        var request = context.Request;
        foreach (var (key, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers[key] = value;
        }

        return request;
    }
}
