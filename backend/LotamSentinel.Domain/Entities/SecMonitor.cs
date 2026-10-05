namespace LotamSentinel.Domain.Entities;

/// <summary>A LOTAM document link discovered on SEC's listing page that
/// wasn't there on a previous check - see SecMonitorService. Deliberately
/// tracks discovery only, not download/import - the human still downloads
/// and imports through the existing, already-trusted Version Comparison
/// upload flow rather than a new server-side file pipeline.</summary>
public class SecDocumentWatch
{
    public long Id { get; set; }
    public string Url { get; set; } = null!;
    public string? FileName { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public bool Dismissed { get; set; }
}
