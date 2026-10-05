using LotamSentinel.Api.Dtos;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

/// <summary>
/// Backs the Release Audit page: a list of every imported release with its
/// section/mismatch counts (replaces the need to run the validation SQL
/// query by hand), and a per-material-code historical view that shows how
/// one material's manufacturer list has looked across every release it has
/// appeared in - the "restricted, anchored" version of historical trending,
/// reached by drilling into a specific material rather than browsing a
/// standalone page with nothing to show until history accumulates.
/// </summary>
[ApiController]
[Route("api/audit")]
public class AuditController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    public AuditController(LotamSentinelDbContext db) => _db = db;

    [HttpGet("releases")]
    public async Task<ActionResult<List<AuditReleaseSummaryDto>>> GetReleases()
    {
        var releases = await _db.LotamReleases
            .OrderByDescending(r => r.ReleaseDate)
            .Select(r => new
            {
                r.Id,
                r.ReleaseDate,
                r.SourceFilename,
                r.ImportedAt,
                SectionCount = r.MaterialSections.Count(),
                MismatchCount = r.MaterialSections.Count(m => m.ValidationStatus == SectionValidationStatus.Mismatch),
            })
            .ToListAsync();

        return Ok(releases.Select(r => new AuditReleaseSummaryDto(
            r.Id, r.ReleaseDate, r.SourceFilename, r.SectionCount, r.MismatchCount, r.ImportedAt)).ToList());
    }

    /// <summary>Distinct material codes across every release (not scoped to
    /// one release, unlike Material Explorer's combobox) - the search list
    /// for picking which material to see history for.</summary>
    [HttpGet("sections")]
    public async Task<ActionResult<List<AuditSectionOptionDto>>> GetSectionOptions([FromQuery] string? search)
    {
        var query = _db.MaterialSections.Include(m => m.MaterialCategory).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(m =>
                EF.Functions.ILike(m.RawDescription, $"%{search}%") ||
                EF.Functions.ILike(m.Code, $"%{search}%") ||
                EF.Functions.ILike(m.MaterialCategory.Name, $"%{search}%"));

        var sections = await query.ToListAsync();

        var distinct = sections
            .GroupBy(m => m.Code)
            .Select(g => new AuditSectionOptionDto(g.Key, $"{g.First().MaterialCategory.Name} - {g.First().RawDescription}"))
            .OrderBy(o => o.Code, new NaturalCodeComparer())
            .Take(200)
            .ToList();

        return Ok(distinct);
    }

    [HttpGet("sections/{code}/history")]
    public async Task<ActionResult<AuditSectionHistoryDto>> GetSectionHistory(string code)
    {
        var sections = await _db.MaterialSections
            .Include(m => m.Release)
            .Include(m => m.MaterialCategory)
            .Include(m => m.Entries).ThenInclude(e => e.SupplierAlias).ThenInclude(a => a.Supplier)
            .Where(m => m.Code == code)
            .ToListAsync();

        var snapshots = sections
            .OrderBy(s => s.Release.ReleaseDate)
            .Select(s => new AuditSectionReleaseSnapshotDto(
                s.Release.Id,
                s.Release.ReleaseDate,
                s.MaterialCategory.Name,
                s.RawDescription,
                s.PrintedTotal,
                s.ExtractedEntryCount,
                s.ValidationStatus.ToString(),
                s.Entries
                    .Select(e => new AuditManufacturerDto(
                        e.SupplierAlias.Supplier?.CanonicalName ?? e.SupplierAlias.RawName,
                        e.DerivedStatus.ToString()))
                    .OrderBy(m => m.SupplierName)
                    .ToList()))
            .ToList();

        return Ok(new AuditSectionHistoryDto(code, snapshots));
    }
}

/// <summary>Sorts material codes like "1.2", "1.10", "27.1" numerically by
/// segment instead of lexically (which would put "1.10" before "1.2").</summary>
internal class NaturalCodeComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        if (x is null || y is null) return string.CompareOrdinal(x, y);
        var xParts = x.Split('.');
        var yParts = y.Split('.');
        for (int i = 0; i < Math.Max(xParts.Length, yParts.Length); i++)
        {
            int xVal = 0, yVal = 0;
            var xHas = i < xParts.Length && int.TryParse(xParts[i], out xVal);
            var yHas = i < yParts.Length && int.TryParse(yParts[i], out yVal);
            if (xHas && yHas)
            {
                var cmp = xVal.CompareTo(yVal);
                if (cmp != 0) return cmp;
            }
            else
            {
                return string.CompareOrdinal(x, y);
            }
        }
        return 0;
    }
}
