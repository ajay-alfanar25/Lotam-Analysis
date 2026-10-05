using LotamSentinel.Api.Dtos;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

/// <summary>
/// The shared "My Suppliers" watchlist (see Domain/Entities/Watchlist.cs for
/// why this isn't per-user - no auth exists in V1).
/// </summary>
[ApiController]
[Route("api/watchlist")]
public class WatchlistController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    public WatchlistController(LotamSentinelDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<WatchlistItemDto>>> GetAll()
    {
        var items = await _db.WatchlistSuppliers
            .Include(w => w.Supplier)
            .OrderByDescending(w => w.AddedAt)
            .ToListAsync();

        return Ok(items.Select(w => new WatchlistItemDto(
            w.Id, w.SupplierId, w.Supplier.CanonicalName, w.Supplier.CanonicalCountry,
            w.AddedBy, w.Notes, w.AddedAt)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<WatchlistItemDto>> Add([FromBody] AddWatchlistRequest request)
    {
        var supplier = await _db.Suppliers.FindAsync(request.SupplierId);
        if (supplier is null) return NotFound("Supplier does not exist.");

        var existing = await _db.WatchlistSuppliers.FirstOrDefaultAsync(w => w.SupplierId == request.SupplierId);
        if (existing is not null) return Conflict("Supplier is already on the watchlist.");

        var item = new WatchlistSupplier
        {
            SupplierId = request.SupplierId,
            AddedBy = request.AddedBy,
            Notes = request.Notes,
            AddedAt = DateTimeOffset.UtcNow,
        };
        _db.WatchlistSuppliers.Add(item);
        await _db.SaveChangesAsync();

        return Ok(new WatchlistItemDto(item.Id, supplier.Id, supplier.CanonicalName, supplier.CanonicalCountry,
            item.AddedBy, item.Notes, item.AddedAt));
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Remove(long id)
    {
        var item = await _db.WatchlistSuppliers.FindAsync(id);
        if (item is null) return NotFound();
        _db.WatchlistSuppliers.Remove(item);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Change records scoped to just the watchlisted suppliers -
    /// reuses change_record directly (the same table Change Explorer reads),
    /// so this can never disagree with what Change Explorer shows for the
    /// same release pair.</summary>
    [HttpGet("changes")]
    public async Task<ActionResult<List<ChangeRecordDto>>> GetWatchlistChanges(
        [FromQuery] long previousReleaseId, [FromQuery] long currentReleaseId)
    {
        var watchlistedSupplierIds = await _db.WatchlistSuppliers.Select(w => w.SupplierId).ToListAsync();
        if (watchlistedSupplierIds.Count == 0) return Ok(new List<ChangeRecordDto>());

        var changes = await _db.ChangeRecords
            .Include(c => c.Supplier)
            .Include(c => c.MaterialSection)
            .Include(c => c.PreviousRelease)
            .Include(c => c.CurrentRelease)
            .Where(c => c.PreviousReleaseId == previousReleaseId && c.CurrentReleaseId == currentReleaseId
                     && c.SupplierId != null && watchlistedSupplierIds.Contains(c.SupplierId.Value))
            .ToListAsync();

        return Ok(changes.Select(c => ChangeMapper.ToDto(c)).ToList());
    }
}
