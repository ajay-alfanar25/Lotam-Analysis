using LotamSentinel.Api.Dtos;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
public class SuppliersController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    public SuppliersController(LotamSentinelDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<SupplierDto>>> GetAll([FromQuery] string? search, [FromQuery] string? status)
    {
        var query = _db.Suppliers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(s => EF.Functions.ILike(s.CanonicalName, $"%{search}%"));

        // Status filters the supplier LIST itself (not just a selected
        // supplier's material table) - "show me suppliers who currently
        // have at least one Suspended material", scoped to the latest
        // release the same way the detail view is.
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ApprovalStatus>(status, true, out var statusEnum))
        {
            var latestReleaseDate = await _db.LotamReleases.MaxAsync(r => (DateOnly?)r.ReleaseDate);
            if (latestReleaseDate is not null)
            {
                var supplierIdsWithStatus = _db.LotamEntries
                    .Where(e => e.MaterialSection.Release.ReleaseDate == latestReleaseDate
                             && e.DerivedStatus == statusEnum
                             && e.SupplierAlias.SupplierId != null)
                    .Select(e => e.SupplierAlias.SupplierId!.Value)
                    .Distinct();
                query = query.Where(s => supplierIdsWithStatus.Contains(s.Id));
            }
        }

        var suppliers = await query.OrderBy(s => s.CanonicalName).Take(200).ToListAsync();
        var aliasCounts = await _db.SupplierAliases
            .Where(a => a.SupplierId != null)
            .GroupBy(a => a.SupplierId)
            .Select(g => new { SupplierId = g.Key!.Value, Count = g.Count() })
            .ToListAsync();
        var countLookup = aliasCounts.ToDictionary(x => x.SupplierId, x => x.Count);

        return Ok(suppliers.Select(s => new SupplierDto(
            s.Id, s.CanonicalName, s.CanonicalCountry,
            countLookup.TryGetValue(s.Id, out var cnt) ? cnt : 0)).ToList());
    }

    /// <summary>Per-material qualification status for this supplier - scoped to
    /// the LATEST ingested release only. Deliberately not full cross-release
    /// history: showing rows from two different files in one table read as
    /// if a company were qualified for the same material twice, when really
    /// each row belongs to a different point in time.</summary>
    [HttpGet("{id:long}/history")]
    public async Task<ActionResult<SupplierHistoryDto>> GetHistory(long id)
    {
        var supplier = await _db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound();

        var latestReleaseDate = await _db.LotamReleases.MaxAsync(r => (DateOnly?)r.ReleaseDate);
        if (latestReleaseDate is null)
            return Ok(new SupplierHistoryDto(supplier.Id, supplier.CanonicalName, new List<SupplierMaterialStatusDto>()));

        var entries = await _db.LotamEntries
            .Include(e => e.MaterialSection).ThenInclude(m => m.Release)
            .Include(e => e.MaterialSection).ThenInclude(m => m.MaterialCategory)
            .Include(e => e.SupplierAlias)
            .Where(e => e.SupplierAlias.SupplierId == id
                     && e.MaterialSection.Release.ReleaseDate == latestReleaseDate)
            .ToListAsync();

        var statuses = entries
            .OrderBy(e => e.MaterialSection.Code)
            .Select(e => new SupplierMaterialStatusDto(
                e.Id, e.MaterialSection.Code, e.MaterialSection.MaterialCategory.Name,
                e.MaterialSection.RawDescription, e.MaterialSection.ApplicableStandard,
                e.MaterialSection.Release.ReleaseDate, e.DerivedStatus.ToString()))
            .ToList();

        return Ok(new SupplierHistoryDto(supplier.Id, supplier.CanonicalName, statuses));
    }

    /// <summary>Full raw traceability record for a single qualification entry -
    /// backs the Supplier View's click-to-detail drill-down.</summary>
    [HttpGet("~/api/lotam-entries/{id:long}")]
    public async Task<ActionResult<LotamEntryDetailDto>> GetEntryDetail(long id)
    {
        var entry = await _db.LotamEntries
            .Include(e => e.MaterialSection).ThenInclude(m => m.Release)
            .Include(e => e.MaterialSection).ThenInclude(m => m.MaterialCategory)
            .Include(e => e.SupplierAlias).ThenInclude(a => a.Supplier)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (entry is null) return NotFound();

        return Ok(new LotamEntryDetailDto(
            entry.Id,
            entry.SupplierAlias.Supplier?.CanonicalName ?? entry.SupplierAlias.RawName,
            entry.MaterialSection.Code,
            entry.MaterialSection.MaterialCategory.Name,
            entry.MaterialSection.RawDescription,
            entry.MaterialSection.ApplicableStandard,
            entry.MaterialSection.Release.ReleaseDate,
            entry.MaterialSection.Release.SourceFilename,
            entry.PageNumber,
            entry.DerivedStatus.ToString(),
            entry.StatusConfidence,
            entry.LocalRepresentativeRaw,
            entry.ModelRatingRaw,
            entry.TypeTestsRaw,
            entry.RemarksRaw,
            entry.ManufacturerCountryRaw,
            entry.RawManufacturerBlock));
    }

    /// <summary>Which specific change types affected this entry within a
    /// release pair - backs Material Explorer's "highlight what changed"
    /// on an Updated row. Matches on either side (an entry can be the
    /// "current" side of the change, or occasionally the "previous" side
    /// if browsing the previous release's tab) so this works from either
    /// tab, not just the current one.</summary>
    [HttpGet("~/api/lotam-entries/{id:long}/change-types")]
    public async Task<ActionResult<List<string>>> GetEntryChangeTypes(
        long id, [FromQuery] long? previousReleaseId, [FromQuery] long? currentReleaseId)
    {
        if (previousReleaseId is null || currentReleaseId is null) return Ok(new List<string>());

        var changeTypes = await _db.ChangeRecords
            .Where(c => c.PreviousReleaseId == previousReleaseId && c.CurrentReleaseId == currentReleaseId
                     && (c.PreviousEntryId == id || c.CurrentEntryId == id))
            .Select(c => c.ChangeType.ToString())
            .Distinct()
            .ToListAsync();

        return Ok(changeTypes);
    }
}
