using LotamSentinel.Api.Dtos;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

/// <summary>
/// Backs the Supply Chain Map: how many distinct qualified suppliers exist
/// per country, per technology, for the latest release. Deliberately reuses
/// lotam_entry.manufacturer_country_normalized and
/// material_section_technology directly rather than maintaining a separate
/// aggregate table - there is exactly one source of truth for "what country
/// is this manufacturer in" and "what technology is this material," and
/// this always reflects it live rather than risking drift.
/// </summary>
[ApiController]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    public AnalyticsController(LotamSentinelDbContext db) => _db = db;

    [HttpGet("geo-concentration")]
    public async Task<ActionResult<GeoConcentrationSummaryDto>> GetGeoConcentration()
    {
        var latestReleaseDate = await _db.LotamReleases.MaxAsync(r => (DateOnly?)r.ReleaseDate);
        if (latestReleaseDate is null)
            return Ok(new GeoConcentrationSummaryDto(default, 0, new List<GeoConcentrationDto>()));

        var latestReleaseId = await _db.LotamReleases
            .Where(r => r.ReleaseDate == latestReleaseDate)
            .Select(r => r.Id)
            .FirstAsync();

        var entries = await _db.LotamEntries
            .Include(e => e.MaterialSection).ThenInclude(m => m.Technologies).ThenInclude(t => t.Technology)
            .Include(e => e.SupplierAlias)
            .Where(e => e.MaterialSection.ReleaseId == latestReleaseId)
            .ToListAsync();

        // Never silently drop entries with no extracted country - count them
        // explicitly so the map's totals can be reconciled against the real
        // record count instead of just looking smaller than expected.
        var unmappedCount = entries.Count(e => string.IsNullOrWhiteSpace(e.ManufacturerCountryNormalized));

        var rows = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.ManufacturerCountryNormalized) && e.SupplierAlias.SupplierId != null)
            .SelectMany(e => e.MaterialSection.Technologies.Select(t => new
            {
                Country = e.ManufacturerCountryNormalized!.Trim().ToUpperInvariant(),
                t.Technology.Code,
                t.Technology.Name,
                SupplierId = e.SupplierAlias.SupplierId!.Value,
            }))
            .GroupBy(x => new { x.Country, x.Code, x.Name })
            .Select(g => new GeoConcentrationDto(
                g.Key.Country, g.Key.Code, g.Key.Name,
                g.Select(x => x.SupplierId).Distinct().Count())) // distinct suppliers, not entry rows
            .OrderByDescending(g => g.SupplierCount)
            .ToList();

        return Ok(new GeoConcentrationSummaryDto(latestReleaseDate.Value, unmappedCount, rows));
    }

    /// <summary>Backs the "click a country or pin" detail panel on Supply
    /// Chain Map - lists the actual suppliers behind a country's count (and
    /// technology filter, if one is active), with local representative and
    /// status. Deliberately light detail ("not much for now") - this is a
    /// quick-glance panel, not a replacement for Material Explorer/Supplier
    /// View, which remain the place for full record detail.</summary>
    [HttpGet("geo-concentration/details")]
    public async Task<ActionResult<List<GeoConcentrationSupplierDto>>> GetGeoConcentrationDetails(
        [FromQuery] string country, [FromQuery] string? technology)
    {
        var latestReleaseDate = await _db.LotamReleases.MaxAsync(r => (DateOnly?)r.ReleaseDate);
        if (latestReleaseDate is null) return Ok(new List<GeoConcentrationSupplierDto>());

        var latestReleaseId = await _db.LotamReleases
            .Where(r => r.ReleaseDate == latestReleaseDate)
            .Select(r => r.Id)
            .FirstAsync();

        var normalizedCountry = country.Trim().ToUpperInvariant();

        var entries = await _db.LotamEntries
            .Include(e => e.MaterialSection).ThenInclude(m => m.Technologies).ThenInclude(t => t.Technology)
            .Include(e => e.SupplierAlias).ThenInclude(a => a.Supplier)
            .Where(e => e.MaterialSection.ReleaseId == latestReleaseId
                     && e.ManufacturerCountryNormalized != null
                     && e.ManufacturerCountryNormalized.Trim().ToUpper() == normalizedCountry)
            .ToListAsync();

        var filtered = string.IsNullOrWhiteSpace(technology)
            ? entries
            : entries.Where(e => e.MaterialSection.Technologies.Any(t => t.Technology.Name == technology)).ToList();

        var result = filtered
            .Select(e => new GeoConcentrationSupplierDto(
                e.Id,
                e.SupplierAlias.Supplier?.CanonicalName ?? e.SupplierAlias.RawName,
                e.LocalRepresentativeRaw,
                e.DerivedStatus.ToString(),
                e.MaterialSection.Code,
                e.MaterialSection.RawDescription))
            .OrderBy(s => s.SupplierName)
            .ToList();

        return Ok(result);
    }
}
