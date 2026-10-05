namespace LotamSentinel.Domain.Entities;

/// <summary>The shared organizational "My Suppliers" watchlist. No per-user
/// scoping - V1 has no auth, so this is one shared list; AddedBy is
/// free-text attribution, not a real account reference.</summary>
public class WatchlistSupplier
{
    public long Id { get; set; }
    public long SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public string AddedBy { get; set; } = null!;
    public string? Notes { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}
