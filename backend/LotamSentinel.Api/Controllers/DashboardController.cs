using LotamSentinel.Api.Dtos;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    public DashboardController(LotamSentinelDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<DashboardSummaryDto>> Get(
        [FromQuery] long previousReleaseId, [FromQuery] long currentReleaseId)
    {
        var previousRelease = await _db.LotamReleases.FindAsync(previousReleaseId);
        var currentRelease = await _db.LotamReleases.FindAsync(currentReleaseId);

        var changes = await _db.ChangeRecords
            .Include(c => c.Supplier)
            .Include(c => c.MaterialSection)
            .Include(c => c.PreviousRelease)
            .Include(c => c.CurrentRelease)
            .Where(c => c.PreviousReleaseId == previousReleaseId && c.CurrentReleaseId == currentReleaseId)
            .ToListAsync();

        var totalRecordsCurrent = await _db.LotamEntries
            .Include(e => e.MaterialSection)
            .Where(e => e.MaterialSection.ReleaseId == currentReleaseId)
            .CountAsync();

        var bySeverity = changes.GroupBy(c => c.Severity).ToDictionary(g => g.Key, g => g.Count());

        var sectionIds = changes.Where(c => c.MaterialSectionId != null)
            .Select(c => c.MaterialSectionId!.Value).Distinct().ToList();
        var techMappings = await _db.MaterialSectionTechnologies
            .Include(mst => mst.Technology)
            .Where(mst => sectionIds.Contains(mst.MaterialSectionId))
            .ToListAsync();
        var techBySection = techMappings.GroupBy(t => t.MaterialSectionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Technology).ToList());

        var byTechnology = new Dictionary<string, (string Name, int Count)>();
        foreach (var c in changes)
        {
            if (c.MaterialSectionId is null) continue;
            if (!techBySection.TryGetValue(c.MaterialSectionId.Value, out var techs)) continue;
            foreach (var t in techs)
            {
                if (!byTechnology.ContainsKey(t.Code)) byTechnology[t.Code] = (t.Name, 0);
                byTechnology[t.Code] = (t.Name, byTechnology[t.Code].Count + 1);
            }
        }

        var byType = changes.GroupBy(c => c.ChangeType)
            .Select(g => new ChangeTypeCountDto(g.Key.ToString(), g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        var recentImportant = changes
            .Where(c => c.Severity == Severity.Critical || c.Severity == Severity.High)
            .OrderByDescending(c => c.Severity == Severity.Critical)
            .ThenBy(c => c.Id)
            .Take(10)
            .Select(c => ChangeMapper.ToDto(c,
                c.MaterialSectionId.HasValue && techBySection.TryGetValue(c.MaterialSectionId.Value, out var t)
                    ? t.Select(x => x.Code).ToList() : null))
            .ToList();

        ReleaseDto? Map(LotamRelease? r) => r is null ? null :
            new ReleaseDto(r.Id, r.ReleaseDate, r.SourceFilename, r.PageCount, r.ProcessingStatus.ToString(), r.ImportedAt);

        var summary = new DashboardSummaryDto(
            Map(previousRelease),
            Map(currentRelease),
            totalRecordsCurrent,
            changes.Count,
            bySeverity.GetValueOrDefault(Severity.Critical),
            bySeverity.GetValueOrDefault(Severity.High),
            bySeverity.GetValueOrDefault(Severity.Medium),
            bySeverity.GetValueOrDefault(Severity.Low),
            byTechnology.Select(kv => new TechnologyChangeCountDto(kv.Key, kv.Value.Name, kv.Value.Count))
                .OrderByDescending(x => x.Count).ToList(),
            byType,
            recentImportant);

        return Ok(summary);
    }
}
