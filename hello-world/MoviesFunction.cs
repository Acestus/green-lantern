using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace HelloWorld;

public sealed class MoviesFunction
{
    private static readonly HttpClient HttpClient = new();
    private static readonly Regex ChartTitleRegex = new(
        @"<h1>\s*Weekend Domestic Box Office\s*(?<date>[^<]+)\s*</h1>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex RowRegex = new(
        @"<tr>\s*<td class=""data"">(?<rank>[^<]+)</td>\s*<td class=""data"">(?<prev>[^<]+)</td>\s*<td>\s*<b><a href=""(?<path>[^""]+)"">(?<title>[^<]+)</a></b>\s*</td>\s*<td class=""data(?: [^""]*)?"">(?<gross>[^<]+)</td>\s*<td class=""data(?: [^""]*)?"">(?<change>[^<]*)</td>\s*<td class=""data"">(?<theaters>[^<]+)</td>\s*<td class=""data"">(?<avg>[^<]+)</td>\s*<td class=""data"">(?<total>[^<]+)</td>\s*<td class=""data"">(?<days>[^<]+)</td>\s*</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private readonly ILogger<MoviesFunction> _logger;

    public MoviesFunction(ILogger<MoviesFunction> logger)
    {
        _logger = logger;
    }

    [Function("MoviesOverview")]
    public async Task<IActionResult> MoviesOverview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "movies/overview")] HttpRequest req)
    {
        const string sourceUrl = "https://www.the-numbers.com/weekend-box-office-chart";
        var generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
            request.Headers.UserAgent.ParseAdd("green-lantern-movies-bot/1.0");

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync();
            var chartDate = ExtractChartDate(html) ?? "latest weekend";
            var currentMovies = ParseCurrentMovies(html, sourceUrl)
                .Take(10)
                .Select(ApplyEditorialVerdict)
                .Select(movie => new
                {
                    rank = movie.Rank,
                    title = movie.Title,
                    gross = movie.Gross,
                    weeklyChange = movie.WeeklyChange,
                    theaters = movie.Theaters,
                    theaterAverage = movie.TheaterAverage,
                    totalGross = movie.TotalGross,
                    daysInRelease = movie.DaysInRelease,
                    url = movie.Url,
                    theaterOrStream = movie.TheaterOrStream,
                    siskelEbertMeter = movie.SiskelEbertMeter,
                    jeremyJohnsStyleNote = movie.JeremyJohnsStyleNote
                })
                .ToList();

            return new OkObjectResult(new
            {
                source = new
                {
                    name = "The Numbers",
                    url = sourceUrl,
                    chartDate
                },
                generatedAtUtc,
                currentMovies,
                lastYearRecommendations = GetLastYearRecommendations()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load movie overview data.");
            return new OkObjectResult(new
            {
                source = new
                {
                    name = "The Numbers",
                    url = sourceUrl,
                    chartDate = "unavailable"
                },
                generatedAtUtc,
                warning = "Failed to load the live box-office chart.",
                detail = ex.Message,
                currentMovies = Array.Empty<object>(),
                lastYearRecommendations = GetLastYearRecommendations()
            });
        }
    }

    private static string? ExtractChartDate(string html)
    {
        var match = ChartTitleRegex.Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["date"].Value.Trim()) : null;
    }

    private static List<MovieBoxOfficeRow> ParseCurrentMovies(string html, string sourceUrl)
    {
        var rows = new List<MovieBoxOfficeRow>();
        foreach (Match match in RowRegex.Matches(html))
        {
            if (!int.TryParse(NormalizeNumber(match.Groups["rank"].Value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank))
            {
                continue;
            }

            rows.Add(new MovieBoxOfficeRow(
                Rank: rank,
                Title: WebUtility.HtmlDecode(match.Groups["title"].Value.Trim()),
                Gross: WebUtility.HtmlDecode(match.Groups["gross"].Value.Trim()),
                WeeklyChange: WebUtility.HtmlDecode(match.Groups["change"].Value.Trim()),
                Theaters: WebUtility.HtmlDecode(match.Groups["theaters"].Value.Trim()),
                TheaterAverage: WebUtility.HtmlDecode(match.Groups["avg"].Value.Trim()),
                TotalGross: WebUtility.HtmlDecode(match.Groups["total"].Value.Trim()),
                DaysInRelease: WebUtility.HtmlDecode(match.Groups["days"].Value.Trim()),
                Url: new Uri(new Uri(sourceUrl), match.Groups["path"].Value.Trim()).ToString()));
        }

        return rows.OrderBy(x => x.Rank).ToList();
    }

    private static MovieBoxOfficeViewModel ApplyEditorialVerdict(MovieBoxOfficeRow row)
    {
        var verdict = row.Rank switch
        {
            <= 2 => ("See in theaters", "Two thumbs up", "Big screen only. Do not wait for the couch."),
            <= 4 => ("Worth a ticket", "Thumbs up", "If the trailer got you, the theater is the right move."),
            <= 6 => ("Your call", "One thumb up", "Could go either way; spectacle still helps."),
            <= 8 => ("Stream later", "Half in the bag", "Probably fine on a couch with snacks."),
            _ => ("Stream first", "Needs a drink", "Save the ticket money unless you are curious.")
        };

        return new MovieBoxOfficeViewModel(
            Rank: row.Rank,
            Title: row.Title,
            Gross: row.Gross,
            WeeklyChange: row.WeeklyChange,
            Theaters: row.Theaters,
            TheaterAverage: row.TheaterAverage,
            TotalGross: row.TotalGross,
            DaysInRelease: row.DaysInRelease,
            Url: row.Url,
            TheaterOrStream: verdict.Item1,
            SiskelEbertMeter: verdict.Item2,
            JeremyJohnsStyleNote: verdict.Item3);
    }

    private static string NormalizeNumber(string value)
    {
        var digits = value.Where(char.IsDigit).ToArray();
        return digits.Length == 0 ? string.Empty : new string(digits);
    }

    private static IReadOnlyList<object> GetLastYearRecommendations()
    {
        return new object[]
        {
            new
            {
                title = "Anora",
                why = "Best Picture winner that is messy, funny, and sharp enough to justify the buzz.",
                award = "2025 Oscars: Best Picture, Best Actress, Best Director, Best Original Screenplay",
                stream = "Hulu",
                streamUrl = "https://www.hulu.com/movie/anora-4ea682c1-f3ff-4f56-bc65-f3dd68a4af68",
                sourceUrl = "https://www.oscars.org/oscars/ceremonies/2025"
            },
            new
            {
                title = "The Brutalist",
                why = "The kind of huge, serious movie that benefits from a room with a real sound system.",
                award = "2025 Oscars: Best Actor, Best Original Score, Best Cinematography",
                stream = "Max",
                streamUrl = "https://www.hbomax.com/movies/brutalist/a56ccde9-e73e-430c-914b-b36af9482fe5",
                sourceUrl = "https://www.oscars.org/oscars/ceremonies/2025"
            },
            new
            {
                title = "Conclave",
                why = "Papal intrigue with a clean runtime and enough tension to make it feel like a premium cable event.",
                award = "2025 Oscars: Best Adapted Screenplay",
                stream = "Prime Video",
                streamUrl = "https://www.amazon.com/Conclave-Edward-Berger/dp/B0DLKMHW18",
                sourceUrl = "https://www.oscars.org/oscars/ceremonies/2025"
            },
            new
            {
                title = "Flow",
                why = "A wordless animated feature that landed hard with critics and won the kind of awards people actually remember.",
                award = "2025 Oscars: Best Animated Feature",
                stream = "Max",
                streamUrl = "https://www.hbomax.com/movies/flow/83df1c2b-db9d-49cd-bf83-9ddbaa7e5e30",
                sourceUrl = "https://www.oscars.org/oscars/ceremonies/2025"
            },
            new
            {
                title = "Dune: Part Two",
                why = "A giant science-fiction event that still feels like the kind of thing you watch with the lights low.",
                award = "2025 Oscars: Best Sound, Best Visual Effects",
                stream = "Max",
                streamUrl = "https://www.hbomax.com/movies/dune-part-two/f0a4f239-0b57-47e2-a39a-54fb96925e61",
                sourceUrl = "https://www.oscars.org/oscars/ceremonies/2025"
            }
        };
    }

    private sealed record MovieBoxOfficeRow(
        int Rank,
        string Title,
        string Gross,
        string WeeklyChange,
        string Theaters,
        string TheaterAverage,
        string TotalGross,
        string DaysInRelease,
        string Url);

    private sealed record MovieBoxOfficeViewModel(
        int Rank,
        string Title,
        string Gross,
        string WeeklyChange,
        string Theaters,
        string TheaterAverage,
        string TotalGross,
        string DaysInRelease,
        string Url,
        string TheaterOrStream,
        string SiskelEbertMeter,
        string JeremyJohnsStyleNote);
}
