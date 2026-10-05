using LotamSentinel.Api.Dtos;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

/// <summary>
/// Backs the two review screens the SRS always intended but V1 shipped
/// without a UI for: confirming/rejecting supplier-alias matches, and
/// confirming material-code renumbering (SEC's document occasionally reuses
/// or shifts a material code between releases - the comparator treats this
/// as REMOVED+NEW by default, per the project's explicit "never auto-infer
/// a renumbering" rule; this is the deliberate, human-confirmed override).
/// </summary>
[ApiController]
[Route("api/data-quality")]
public class DataQualityController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    public DataQualityController(LotamSentinelDbContext db) => _db = db;

    // ---------------- Supplier alias review ----------------

    [HttpGet("aliases/needs-review")]
    public async Task<ActionResult<List<SupplierAliasReviewDto>>> GetAliasesNeedingReview()
    {
        var aliases = await _db.SupplierAliases
            .Include(a => a.Supplier)
            .Include(a => a.FirstSeenRelease)
            .Where(a => a.ReviewStatus == ReviewStatus.NeedsReview)
            .OrderByDescending(a => a.CreatedAt)
            .Take(500) // review queue, not a report - keep it to a manageable batch
            .ToListAsync();

        var aliasIds = aliases.Select(a => a.Id).ToList();
        var entryCounts = await _db.LotamEntries
            .Where(e => aliasIds.Contains(e.SupplierAliasId))
            .GroupBy(e => e.SupplierAliasId)
            .Select(g => new { AliasId = g.Key, Count = g.Count() })
            .ToListAsync();
        var countLookup = entryCounts.ToDictionary(x => x.AliasId, x => x.Count);

        return Ok(aliases.Select(a => new SupplierAliasReviewDto(
            a.Id, a.RawName, a.RawCountry,
            a.SupplierId, a.Supplier?.CanonicalName, a.Confidence,
            a.FirstSeenRelease.ReleaseDate,
            countLookup.TryGetValue(a.Id, out var c) ? c : 0)).ToList());
    }

    /// <summary>Search existing suppliers, for the "link this alias to..." picker.</summary>
    [HttpGet("suppliers/search")]
    public async Task<ActionResult<List<SupplierOptionDto>>> SearchSuppliers([FromQuery] string? q)
    {
        var query = _db.Suppliers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(s => EF.Functions.ILike(s.CanonicalName, $"%{q}%"));

        var suppliers = await query.OrderBy(s => s.CanonicalName).Take(50).ToListAsync();
        return Ok(suppliers.Select(s => new SupplierOptionDto(s.Id, s.CanonicalName, s.CanonicalCountry)).ToList());
    }

    [HttpPost("aliases/{id:long}/confirm")]
    public async Task<IActionResult> ConfirmAlias(long id, [FromBody] ConfirmAliasRequest request)
    {
        var alias = await _db.SupplierAliases.FindAsync(id);
        if (alias is null) return NotFound();
        if (!await _db.Suppliers.AnyAsync(s => s.Id == request.SupplierId))
            return BadRequest("Target supplier does not exist.");

        alias.SupplierId = request.SupplierId;
        alias.ReviewStatus = ReviewStatus.Confirmed;
        alias.MatchSource = DerivationSource.Manual;
        alias.Confidence = 1.0m;
        alias.ReviewedBy = request.ReviewedBy;
        alias.ReviewedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Rejects the current (possibly auto-suggested) link. The alias
    /// stays unlinked (SupplierId = null) rather than being deleted - it's
    /// still real, observed data from the source PDF.</summary>
    [HttpPost("aliases/{id:long}/reject")]
    public async Task<IActionResult> RejectAlias(long id, [FromBody] RejectAliasRequest request)
    {
        var alias = await _db.SupplierAliases.FindAsync(id);
        if (alias is null) return NotFound();

        alias.SupplierId = null;
        alias.ReviewStatus = ReviewStatus.Rejected;
        alias.ReviewedBy = request.ReviewedBy;
        alias.ReviewedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>For a genuinely new manufacturer with no existing canonical
    /// match - creates the Supplier and confirms the link in one step.</summary>
    [HttpPost("aliases/{id:long}/create-supplier")]
    public async Task<IActionResult> CreateSupplierForAlias(long id, [FromBody] CreateSupplierForAliasRequest request)
    {
        var alias = await _db.SupplierAliases.FindAsync(id);
        if (alias is null) return NotFound();

        var supplier = new Supplier
        {
            CanonicalName = request.CanonicalName.Trim(),
            CanonicalCountry = request.CanonicalCountry,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        alias.SupplierId = supplier.Id;
        alias.ReviewStatus = ReviewStatus.Confirmed;
        alias.MatchSource = DerivationSource.Manual;
        alias.Confidence = 1.0m;
        alias.ReviewedBy = request.ReviewedBy;
        alias.ReviewedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---------------- Material code remap ----------------

    /// <summary>Candidates for a manual remap: material codes present in the
    /// previous release but not the current one, and vice versa. Purely
    /// informational - creating a remap is a separate, explicit action.</summary>
    [HttpGet("material-sections/unmatched")]
    public async Task<ActionResult<UnmatchedSectionsDto>> GetUnmatchedSections(
        [FromQuery] long previousReleaseId, [FromQuery] long currentReleaseId)
    {
        var previousSections = await _db.MaterialSections
            .Where(m => m.ReleaseId == previousReleaseId)
            .ToListAsync();
        var currentSections = await _db.MaterialSections
            .Where(m => m.ReleaseId == currentReleaseId)
            .ToListAsync();

        var currentCodes = currentSections.Select(c => c.Code).ToHashSet();
        var previousCodes = previousSections.Select(p => p.Code).ToHashSet();

        var removed = previousSections.Where(p => !currentCodes.Contains(p.Code))
            .Select(p => new UnmatchedMaterialSectionDto(p.Id, p.Code, p.RawDescription, p.ExtractedEntryCount))
            .OrderBy(x => x.Code).ToList();
        var added = currentSections.Where(c => !previousCodes.Contains(c.Code))
            .Select(c => new UnmatchedMaterialSectionDto(c.Id, c.Code, c.RawDescription, c.ExtractedEntryCount))
            .OrderBy(x => x.Code).ToList();

        return Ok(new UnmatchedSectionsDto(removed, added));
    }

    [HttpGet("material-code-remaps")]
    public async Task<ActionResult<List<MaterialCodeRemapDto>>> GetRemaps(
        [FromQuery] long previousReleaseId, [FromQuery] long currentReleaseId)
    {
        var remaps = await _db.MaterialCodeRemaps
            .Include(r => r.FromMaterialSection)
            .Include(r => r.ToMaterialSection)
            .Where(r => r.FromMaterialSection.ReleaseId == previousReleaseId
                     && r.ToMaterialSection.ReleaseId == currentReleaseId)
            .ToListAsync();

        return Ok(remaps.Select(r => new MaterialCodeRemapDto(
            r.Id, r.FromMaterialSection.Code, r.ToMaterialSection.Code, r.Rationale, r.ConfirmedBy, r.ConfirmedAt))
            .ToList());
    }

    [HttpPost("material-code-remaps")]
    public async Task<ActionResult<MaterialCodeRemapDto>> CreateRemap([FromBody] CreateRemapRequest request)
    {
        if (request.FromMaterialSectionId == request.ToMaterialSectionId)
            return BadRequest("A material section cannot be remapped to itself.");

        var from = await _db.MaterialSections.FindAsync(request.FromMaterialSectionId);
        var to = await _db.MaterialSections.FindAsync(request.ToMaterialSectionId);
        if (from is null || to is null) return NotFound("One or both material sections do not exist.");

        var remap = new MaterialCodeRemap
        {
            FromMaterialSectionId = from.Id,
            ToMaterialSectionId = to.Id,
            Rationale = request.Rationale,
            ConfirmedBy = request.ConfirmedBy,
            ConfirmedAt = DateTimeOffset.UtcNow
        };
        _db.MaterialCodeRemaps.Add(remap);
        await _db.SaveChangesAsync();

        return Ok(new MaterialCodeRemapDto(remap.Id, from.Code, to.Code, remap.Rationale, remap.ConfirmedBy, remap.ConfirmedAt));
    }
}
