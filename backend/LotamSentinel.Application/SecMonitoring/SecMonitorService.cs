using AngleSharp;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LotamSentinel.Application.SecMonitoring;

public class SecMonitorOptions
{
    /// <summary>Off by default - see PressReleaseSearchOptions for why
    /// external dependencies default off rather than silently no-op-ing.</summary>
    public bool Enabled { get; set; } = false;

    // ---- On hold: LOTAM is behind a login, not publicly searchable. ----
    // Confirmed via se.com.sa/rehabilitation-manufacturers/FAQ.aspx: access
    // to "the list of technically acceptable manufacturers and manufacturer
    // plants" (LOTAM) is a benefit of a registered SRM/e-Bidding account on
    // SEC's prequalification portal, not something published to the open
    // web. Both Google Custom Search (now closed to new customers anyway)
    // and Vertex AI Search were tried and confirmed unable to find it - not
    // because of bad keywords or indexing lag, but because a search engine
    // can only find pages it's allowed to crawl, and this one requires a
    // login. Every "candidate" either API found was a DIFFERENT, publicly-
    // hosted SEC document (the separate "Prequalified Manufacturers/
    // Suppliers for Distribution Networks Materials" list), never LOTAM.
    //
    // The only reliable path forward is an authenticated portal integration
    // (log in with real SEC/SRM credentials and download the file directly)
    // - deliberately NOT built yet, pending Alfanar getting the right kind
    // of portal access/credentials to automate safely. Until then: LOTAM
    // PDFs are downloaded manually from the portal and imported through the
    // existing Version Comparison upload flow - see README.
    //
    // ListingPageUrl below is kept as a free, zero-API-cost fallback for
    // the narrow case where a PUBLICLY reachable page ever does link
    // directly to a current LOTAM file (e.g. a one-off SEC announcement) -
    // it costs nothing to leave configured and harmless to leave blank.
    public string ListingPageUrl { get; set; } = "";

    /// <summary>Only links whose URL contains one of these (case-insensitive)
    /// are treated as candidate LOTAM documents - keeps this from picking up
    /// unrelated files hosted in the same document library folder.</summary>
    public List<string> LinkKeywords { get; set; } = new() { "QUALIFIED-MANUFACTURERS", "QUALIFIED MANUFACTURERS", "LOTAM" };
}

public record SecMonitorCheckResult(bool Configured, string? Message, int NewDocumentsFound, DateTimeOffset CheckedAt);

public interface ISecMonitorService
{
    Task<SecMonitorCheckResult> CheckForNewReleasesAsync(CancellationToken ct = default);
}

public class SecMonitorService : ISecMonitorService
{
    private readonly HttpClient _http;
    private readonly LotamSentinelDbContext _db;
    private readonly IOptionsMonitor<SecMonitorOptions> _optionsMonitor;
    private readonly ILogger<SecMonitorService> _logger;

    // A property, not a field captured once in the constructor - reads the
    // LIVE current value every time it's accessed. IOptions<T> (used
    // before) is a singleton snapshot taken once at app startup and never
    // refreshed, so an appsettings.json edit made while the app was already
    // running would silently never take effect without a full process
    // restart. IOptionsMonitor<T> picks up file changes live.
    private SecMonitorOptions _options => _optionsMonitor.CurrentValue;

    public SecMonitorService(
        HttpClient http, LotamSentinelDbContext db, IOptionsMonitor<SecMonitorOptions> optionsMonitor,
        ILogger<SecMonitorService> logger)
    {
        _http = http;
        _db = db;
        _optionsMonitor = optionsMonitor;
        _logger = logger;

        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "LotamSentinelMonitor/1.0 (internal tool; contact: set-a-real-contact-email@yourcompany.com)");
    }

    public async Task<SecMonitorCheckResult> CheckForNewReleasesAsync(CancellationToken ct = default)
    {
        var useListing = !string.IsNullOrWhiteSpace(_options.ListingPageUrl);

        if (!_options.Enabled || !useListing)
        {
            return new SecMonitorCheckResult(
                Configured: false,
                Message: "Automatic SEC monitoring is on hold. LOTAM is only available behind a login on " +
                         "SEC's prequalification/rehabilitation-manufacturers portal - it isn't publicly " +
                         "crawlable, so no search API (Google Custom Search or Vertex AI Search) can find " +
                         "it. For now, download the LOTAM PDF manually from the portal and import it via " +
                         "Version Comparison. If a publicly-reachable page that links directly to a current " +
                         "LOTAM file is ever found, set SecMonitor:ListingPageUrl and SecMonitor:Enabled=true " +
                         "to scan it automatically - see README.",
                NewDocumentsFound: 0,
                CheckedAt: DateTimeOffset.UtcNow);
        }

        try
        {
            var candidateLinks = await DiscoverViaListingPageAsync(ct);

            var knownUrls = await _db.SecDocumentWatches.Select(w => w.Url).ToListAsync(ct);
            var knownSet = knownUrls.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var newLinks = candidateLinks.Where(l => !knownSet.Contains(l)).ToList();

            foreach (var link in newLinks)
            {
                _db.SecDocumentWatches.Add(new SecDocumentWatch
                {
                    Url = link,
                    FileName = link.Split('/').LastOrDefault(),
                    FirstSeenAt = DateTimeOffset.UtcNow,
                    Dismissed = false,
                });
            }

            if (newLinks.Count > 0)
                await _db.SaveChangesAsync(ct);

            return new SecMonitorCheckResult(true, null, newLinks.Count, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SEC monitor check failed");
            return new SecMonitorCheckResult(
                true, $"The check failed: {ex.Message}. This does not affect any imported LOTAM data.",
                0, DateTimeOffset.UtcNow);
        }
    }

    private async Task<List<string>> DiscoverViaListingPageAsync(CancellationToken ct)
    {
        var html = await _http.GetStringAsync(_options.ListingPageUrl, ct);

        var browsingContext = BrowsingContext.New(Configuration.Default);
        var document = await browsingContext.OpenAsync(req => req.Content(html), ct);

        return document.QuerySelectorAll("a")
            .Select(a => a.GetAttribute("href"))
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .Select(href => href!)
            .Where(href => href.Contains(".ashx", StringComparison.OrdinalIgnoreCase))
            .Where(href => _options.LinkKeywords.Count == 0
                || _options.LinkKeywords.Any(k => href.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Select(ResolveUrl)
            .Distinct()
            .ToList();
    }

    private string ResolveUrl(string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute)) return absolute.ToString();
        return Uri.TryCreate(new Uri(_options.ListingPageUrl), href, out var resolved)
            ? resolved.ToString()
            : href;
    }
}
