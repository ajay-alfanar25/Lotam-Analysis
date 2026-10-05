using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LotamSentinel.Application.PressReleases;

public class PressReleaseSearchOptions
{
    /// <summary>Off by default - a flaky/unconfigured external dependency
    /// should never silently degrade the rest of the app. Must be
    /// explicitly turned on once a real API key is set.</summary>
    public bool Enabled { get; set; } = false;
    public string GoogleApiKey { get; set; } = "";
    public string GoogleSearchEngineId { get; set; } = "";
}

public record PressReleaseHit(string Title, string Snippet, string Url);

public record PressReleaseSearchResult(bool Configured, string? Message, List<PressReleaseHit> Hits);

public interface IPressReleaseSearchService
{
    Task<PressReleaseSearchResult> SearchAsync(
        string materialCode, string categoryName, string description, string? standard,
        CancellationToken ct = default);
}

/// <summary>
/// Looks up recent web mentions of a specific LOTAM material - the point is
/// to catch a supplier that got conditional approval or was announced via
/// press release AFTER the last LOTAM PDF was published, which the parsed
/// document data structurally cannot know about.
///
/// CRITICAL ACCURACY BOUNDARY: this NEVER parses search results into
/// structured claims like "Supplier X is approved." A press snippet is not
/// verified data the way a parsed LOTAM entry is - auto-extracting "facts"
/// from noisy web text and presenting them next to real qualification data
/// would be exactly the kind of confident-but-wrong output this whole
/// project exists to avoid. Results are always raw title/snippet/link for a
/// human to read and judge themselves.
/// </summary>
public class GooglePressReleaseSearchService : IPressReleaseSearchService
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<PressReleaseSearchOptions> _optionsMonitor;
    private readonly ILogger<GooglePressReleaseSearchService> _logger;

    // See SecMonitorService for why this is IOptionsMonitor (live-refreshing)
    // rather than IOptions (a one-time snapshot from app startup).
    private PressReleaseSearchOptions _options => _optionsMonitor.CurrentValue;

    public GooglePressReleaseSearchService(
        HttpClient http, IMemoryCache cache, IOptionsMonitor<PressReleaseSearchOptions> optionsMonitor,
        ILogger<GooglePressReleaseSearchService> logger)
    {
        _http = http;
        _cache = cache;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task<PressReleaseSearchResult> SearchAsync(
        string materialCode, string categoryName, string description, string? standard,
        CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.GoogleApiKey) || string.IsNullOrWhiteSpace(_options.GoogleSearchEngineId))
        {
            return new PressReleaseSearchResult(
                Configured: false,
                Message: "Press release search isn't configured yet. Set PressReleaseSearch:GoogleApiKey, " +
                         "PressReleaseSearch:GoogleSearchEngineId, and PressReleaseSearch:Enabled=true in " +
                         "appsettings.Development.json - see README for setup steps. Free up to 100 " +
                         "queries/day, which covers normal Tendering usage.",
                Hits: new List<PressReleaseHit>());
        }

        var query = BuildQuery(materialCode, categoryName, description, standard);
        var cacheKey = $"press-release-search:{query}";

        if (_cache.TryGetValue(cacheKey, out PressReleaseSearchResult? cached) && cached is not null)
            return cached;

        try
        {
            var url = "https://www.googleapis.com/customsearch/v1"
                + $"?key={Uri.EscapeDataString(_options.GoogleApiKey)}"
                + $"&cx={Uri.EscapeDataString(_options.GoogleSearchEngineId)}"
                + "&num=5"
                + $"&q={Uri.EscapeDataString(query)}";

            var apiResponse = await _http.GetFromJsonAsync<GoogleSearchApiResponse>(url, ct);

            var hits = (apiResponse?.Items ?? new List<GoogleSearchApiItem>())
                .Select(i => new PressReleaseHit(i.Title ?? "(untitled)", i.Snippet ?? "", i.Link ?? ""))
                .Where(h => !string.IsNullOrWhiteSpace(h.Url))
                .ToList();

            var result = new PressReleaseSearchResult(true, null, hits);
            _cache.Set(cacheKey, result, TimeSpan.FromHours(24));
            return result;
        }
        catch (Exception ex)
        {
            // A flaky external search must never break the Tender Readiness
            // page or look like a real "no press mentions" result - both are
            // wrong. Say plainly that the search itself failed.
            _logger.LogWarning(ex, "Press release search failed for query: {Query}", query);
            return new PressReleaseSearchResult(
                Configured: true,
                Message: "The search request failed - this does not affect any LOTAM data on this page. " +
                         "Try again shortly.",
                Hits: new List<PressReleaseHit>());
        }
    }

    /// <summary>
    /// The structured query-construction algorithm: combine the material's
    /// own identity (category, description, applicable standard) with SEC
    /// context terms and intent keywords that real prequalification
    /// announcements tend to use, plus the current year for recency.
    /// Deliberately generic across material types - no per-technology
    /// special-casing - so it generalizes to every material a Tendering user
    /// might look up, not just the ones seen during development.
    /// </summary>
    private static string BuildQuery(string materialCode, string categoryName, string description, string? standard)
    {
        var parts = new List<string>
        {
            "(\"Saudi Electricity Company\" OR \"National Grid SA\" OR SEC)",
            $"\"{categoryName}\"",
        };
        if (!string.IsNullOrWhiteSpace(description)) parts.Add(description);
        if (!string.IsNullOrWhiteSpace(standard)) parts.Add(standard);
        parts.Add("(press release OR announcement OR prequalified OR \"conditional approval\" OR LOTAM)");
        parts.Add(DateTime.UtcNow.Year.ToString());

        var query = string.Join(' ', parts);
        // Keep well under any search API's practical query-length limits -
        // a long material description should never produce a broken query.
        return query.Length > 500 ? query[..500] : query;
    }

    private class GoogleSearchApiResponse
    {
        [JsonPropertyName("items")]
        public List<GoogleSearchApiItem>? Items { get; set; }
    }

    private class GoogleSearchApiItem
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("snippet")]
        public string? Snippet { get; set; }
        [JsonPropertyName("link")]
        public string? Link { get; set; }
    }
}
