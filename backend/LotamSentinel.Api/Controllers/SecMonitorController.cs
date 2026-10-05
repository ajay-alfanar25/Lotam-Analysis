using LotamSentinel.Api.Dtos;
using LotamSentinel.Application.SecMonitoring;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

/// <summary>
/// Manual "check now" trigger (shares ISecMonitorService with the daily
/// background job, so results can't disagree) and the list of discovered-
/// but-not-yet-handled documents shown on the Release Audit page.
/// </summary>
[ApiController]
[Route("api/sec-monitor")]
public class SecMonitorController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    private readonly ISecMonitorService _monitor;

    public SecMonitorController(LotamSentinelDbContext db, ISecMonitorService monitor)
    {
        _db = db;
        _monitor = monitor;
    }

    [HttpPost("check-now")]
    public async Task<ActionResult<SecMonitorCheckResultDto>> CheckNow(CancellationToken ct)
    {
        var result = await _monitor.CheckForNewReleasesAsync(ct);
        return Ok(new SecMonitorCheckResultDto(
            result.Configured, result.Message, result.NewDocumentsFound, result.CheckedAt));
    }

    [HttpGet("pending")]
    public async Task<ActionResult<List<SecDocumentWatchDto>>> GetPending()
    {
        var docs = await _db.SecDocumentWatches
            .Where(w => !w.Dismissed)
            .OrderByDescending(w => w.FirstSeenAt)
            .ToListAsync();

        return Ok(docs.Select(d => new SecDocumentWatchDto(d.Id, d.Url, d.FileName, d.FirstSeenAt)).ToList());
    }

    /// <summary>Marks a discovered link as handled (already downloaded and
    /// imported, or intentionally not relevant) so it stops showing as
    /// pending. Doesn't touch any LOTAM data - purely bookkeeping.</summary>
    [HttpPost("{id:long}/dismiss")]
    public async Task<IActionResult> Dismiss(long id)
    {
        var doc = await _db.SecDocumentWatches.FindAsync(id);
        if (doc is null) return NotFound();
        doc.Dismissed = true;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Permanently removes a discovered-link row - for a genuine
    /// false positive (wrong document entirely) rather than something
    /// legitimately handled. Unlike Dismiss, this doesn't just hide the
    /// row: it's gone, and if the same URL is ever found again by a later
    /// check it will show up again as "new". Doesn't touch any LOTAM
    /// data - purely bookkeeping.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var doc = await _db.SecDocumentWatches.FindAsync(id);
        if (doc is null) return NotFound();
        _db.SecDocumentWatches.Remove(doc);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
