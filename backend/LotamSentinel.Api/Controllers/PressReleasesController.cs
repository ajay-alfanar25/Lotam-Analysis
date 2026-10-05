using LotamSentinel.Api.Dtos;
using LotamSentinel.Application.PressReleases;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

[ApiController]
[Route("api/press-releases")]
public class PressReleasesController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    private readonly IPressReleaseSearchService _search;

    public PressReleasesController(LotamSentinelDbContext db, IPressReleaseSearchService search)
    {
        _db = db;
        _search = search;
    }

    /// <summary>Backs the "recent press mentions" box on Tender Readiness.
    /// Looks up the material's own identity from material_section (the same
    /// row Material Explorer / Tender Readiness already show) and searches
    /// the web for recent, unofficial mentions - never LOTAM data itself.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<PressReleaseSearchResponseDto>> Search(
        [FromQuery] long materialSectionId, CancellationToken ct)
    {
        var section = await _db.MaterialSections.Include(m => m.MaterialCategory)
            .FirstOrDefaultAsync(m => m.Id == materialSectionId, ct);
        if (section is null) return NotFound();

        var result = await _search.SearchAsync(
            section.Code, section.MaterialCategory.Name, section.RawDescription, section.ApplicableStandard, ct);

        return Ok(new PressReleaseSearchResponseDto(
            result.Configured,
            result.Message,
            result.Hits.Select(h => new PressReleaseResultDto(h.Title, h.Snippet, h.Url)).ToList()));
    }
}
