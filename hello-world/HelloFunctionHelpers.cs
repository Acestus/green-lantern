using Microsoft.AspNetCore.Http;
using System.Globalization;
namespace HelloWorld;

internal static class HelloFunctionHelpers
{
    internal static string GetCorrelationId(HttpRequest req)
    {
        var header = req.Headers["x-correlation-id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header))
        {
            return header;
        }

        return Guid.NewGuid().ToString();
    }

    internal static string ToReportTitle(string blobName)
    {
        var noExt = blobName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? blobName[..^5]
            : blobName;

        var words = noExt.Replace('-', ' ').Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words);
    }

    internal static double ToDouble(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return 0;
    }
}
