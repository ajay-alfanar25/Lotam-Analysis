using LotamSentinel.Api.Dtos;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

[ApiController]
[Route("api/technologies")]
public class TechnologiesController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    public TechnologiesController(LotamSentinelDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<TechnologyDto>>> GetAll()
    {
        var techs = await _db.Technologies
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync();
        return Ok(techs.Select(t => new TechnologyDto(t.Id, t.Code, t.Name, t.Description)).ToList());
    }

    /// <summary>All changes (for a given release pair) whose material section
    /// is classified under this technology.</summary>
    [HttpGet("{code}/changes")]
    public async Task<ActionResult<List<ChangeRecordDto>>> GetChanges(
        string code, [FromQuery] long previousReleaseId, [FromQuery] long currentReleaseId)
    {
        var tech = await _db.Technologies.FirstOrDefaultAsync(t => t.Code == code);
        if (tech is null) return NotFound();

        var sectionIds = await _db.MaterialSectionTechnologies
            .Where(mst => mst.TechnologyId == tech.Id)
            .Select(mst => mst.MaterialSectionId)
            .ToListAsync();

        var changes = await _db.ChangeRecords
            .Include(c => c.Supplier)
            .Include(c => c.MaterialSection)
            .Include(c => c.PreviousRelease)
            .Include(c => c.CurrentRelease)
            .Where(c => c.PreviousReleaseId == previousReleaseId
                     && c.CurrentReleaseId == currentReleaseId
                     && c.MaterialSectionId != null
                     && sectionIds.Contains(c.MaterialSectionId!.Value))
            .ToListAsync();

        return Ok(changes.Select(c => ChangeMapper.ToDto(c)).ToList());
    }
}
