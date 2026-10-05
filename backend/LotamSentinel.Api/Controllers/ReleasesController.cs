using LotamSentinel.Api.Dtos;
using LotamSentinel.Application.Comparison;
using LotamSentinel.Application.Email;
using LotamSentinel.Application.Reporting;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

[ApiController]
[Route("api/releases")]
public class ReleasesController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    private readonly MaterialReportBuilder _reportBuilder;
    private readonly IReportRendererFactory _rendererFactory;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<ReleasesController> _logger;

    public ReleasesController(
        LotamSentinelDbContext db, MaterialReportBuilder reportBuilder,
        IReportRendererFactory rendererFactory, IEmailSender emailSender, ILogger<ReleasesController> logger)
    {
        _db = db;
        _reportBuilder = reportBuilder;
        _rendererFactory = rendererFactory;
        _emailSender = emailSender;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<ReleaseDto>>> GetAll()
    {
        // Materialize first, then project in-memory - avoids relying on the
        // provider being able to translate enum.ToString() server-side.
        var entities = await _db.LotamReleases
            .OrderByDescending(r => r.ReleaseDate)
            .ToListAsync();

        var releases = entities.Select(r => new ReleaseDto(
            r.Id, r.ReleaseDate, r.SourceFilename, r.PageCount, r.ProcessingStatus.ToString(), r.ImportedAt)).ToList();
        return Ok(releases);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ReleaseDto>> GetById(long id)
    {
        var r = await _db.LotamReleases.FindAsync(id);
        if (r is null) return NotFound();
        return Ok(new ReleaseDto(r.Id, r.ReleaseDate, r.SourceFilename, r.PageCount,
            r.ProcessingStatus.ToString(), r.ImportedAt));
    }

    /// <summary>Every material section in this one release, labeled exactly as
    /// the document groups it (category + description + standard) - backs the
    /// Material Explorer's searchable dropdown. Deliberately NOT filtered by
    /// our own technology taxonomy; this is the document's own grouping.</summary>
    [HttpGet("{id:long}/material-sections")]
    public async Task<ActionResult<List<MaterialSectionOptionDto>>> GetMaterialSections(long id)
    {
        var sections = await _db.MaterialSections
            .Include(m => m.MaterialCategory)
            .Where(m => m.ReleaseId == id)
            .ToListAsync();

        var options = sections
            .Select(m => new MaterialSectionOptionDto(
                m.Id,
                m.Code,
                $"{m.MaterialCategory.Name} {m.RawDescription}"
                    + (string.IsNullOrWhiteSpace(m.ApplicableStandard) ? "" : $" — Applicable Standards: {m.ApplicableStandard}"),
                m.ExtractedEntryCount,
                m.PrintedTotal,
                m.ValidationStatus.ToString()))
            .OrderBy(o => ParseSortKey(o.Code))
            .ToList();

        return Ok(options);
    }

    // Sorts "1.1", "1.10", "2.1" numerically by both segments rather than
    // as plain text (which would put "1.10" before "1.2").
    private static (int, int) ParseSortKey(string code)
    {
        var parts = code.Split('.', 2);
        int.TryParse(parts[0], out var a);
        int.TryParse(parts.Length > 1 ? parts[1] : "0", out var b);
        return (a, b);
    }

    /// <summary>Backs Material Explorer's default flat supplier list -
    /// lightweight rows (no raw manufacturer text, just what's needed for a
    /// collapsed row) for every entry in this release, optionally narrowed
    /// to specific material codes. One call instead of one per material
    /// section, which matters here since the default view can span every
    /// material in the release. Full detail per row is fetched separately,
    /// lazily, via the existing GET /api/lotam-entries/{id} endpoint when a
    /// row is actually expanded - unchanged, reused as-is.</summary>
    [HttpGet("{id:long}/entries-summary")]
    public async Task<ActionResult<List<EntrySummaryDto>>> GetEntriesSummary(
        long id, [FromQuery] string? materialCodes,
        [FromQuery] long? previousReleaseId, [FromQuery] long? currentReleaseId)
    {
        var codesFilter = string.IsNullOrWhiteSpace(materialCodes)
            ? null
            : materialCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

        var query = _db.LotamEntries
            .Include(e => e.MaterialSection).ThenInclude(m => m.MaterialCategory)
            .Include(e => e.SupplierAlias).ThenInclude(a => a.Supplier)
            .Where(e => e.MaterialSection.ReleaseId == id);

        if (codesFilter is not null)
            query = query.Where(e => codesFilter.Contains(e.MaterialSection.Code));

        var entries = await query.ToListAsync();

        // Same badge logic as MaterialSectionsController.GetDetail, batched
        // across every section in this release in one pass instead of one
        // query per section - see that controller for why "Updated" covers
        // this specific set of change types.
        var updateChangeTypes = new[]
        {
            ChangeType.StatusChanged, ChangeType.ModelAdded, ChangeType.ModelRemoved,
            ChangeType.RatingChanged, ChangeType.VoltageChanged, ChangeType.SpecificationChanged,
            ChangeType.ConditionChanged, ChangeType.RemarkChanged, ChangeType.AgentChanged,
            ChangeType.CountryChanged, ChangeType.ScopeChanged, ChangeType.OtherMaterialChange,
        };

        HashSet<long> newIds = new(), removedIds = new(), updatedIds = new();

        if (previousReleaseId is not null && currentReleaseId is not null)
        {
            var pairChanges = await _db.ChangeRecords
                .Where(c => c.PreviousReleaseId == previousReleaseId && c.CurrentReleaseId == currentReleaseId)
                .ToListAsync();

            if (id == currentReleaseId)
            {
                newIds = pairChanges.Where(c => c.ChangeType == ChangeType.NewRecord && c.CurrentEntryId != null)
                    .Select(c => c.CurrentEntryId!.Value).ToHashSet();
                updatedIds = pairChanges.Where(c => updateChangeTypes.Contains(c.ChangeType) && c.CurrentEntryId != null)
                    .Select(c => c.CurrentEntryId!.Value).ToHashSet();
            }
            else if (id == previousReleaseId)
            {
                removedIds = pairChanges.Where(c => c.ChangeType == ChangeType.RemovedRecord && c.PreviousEntryId != null)
                    .Select(c => c.PreviousEntryId!.Value).ToHashSet();
                updatedIds = pairChanges.Where(c => updateChangeTypes.Contains(c.ChangeType) && c.PreviousEntryId != null)
                    .Select(c => c.PreviousEntryId!.Value).ToHashSet();
            }
        }

        var result = entries
            .Select(e => new EntrySummaryDto(
                e.Id,
                e.MaterialSection.Code,
                $"{e.MaterialSection.MaterialCategory.Name} {e.MaterialSection.RawDescription}",
                e.SupplierAlias.Supplier?.CanonicalName ?? e.SupplierAlias.RawName,
                e.DerivedStatus.ToString(),
                newIds.Contains(e.Id) ? "New"
                    : removedIds.Contains(e.Id) ? "Removed"
                    : updatedIds.Contains(e.Id) ? "Updated"
                    : null,
                e.MaterialSection.MaterialCategory.Name,
                e.MaterialSection.RawDescription,
                e.MaterialSection.ApplicableStandard))
            .OrderBy(x => ParseSortKey(x.MaterialCode))
            .ThenBy(x => x.SupplierName)
            .ToList();

        return Ok(result);
    }

    /// <summary>Generates the material report on the fly and either streams
    /// it directly in the response (format=pdf|html, the default) or emails
    /// it as an attachment when emailTo is given - nothing is ever written
    /// to disk either way.</summary>
    [HttpGet("{id:long}/entries-export")]
    public async Task<IActionResult> ExportEntries(
        long id, [FromQuery] string? materialCodes,
        [FromQuery] long? previousReleaseId, [FromQuery] long? currentReleaseId,
        [FromQuery] string? format, [FromQuery] string? emailTo, CancellationToken ct)
    {
        var codes = string.IsNullOrWhiteSpace(materialCodes)
            ? null
            : materialCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        ExportReport report;
        IReportRenderer renderer;
        byte[] bytes;
        string filename;
        try
        {
            report = await _reportBuilder.BuildAsync(id, codes, previousReleaseId, currentReleaseId, ct);
            renderer = _rendererFactory.Get(format);
            bytes = renderer.Render(report);
            filename = $"LOTAM-Sentinel-Materials-{DateTime.UtcNow:yyyy-MM-dd}.{renderer.FileExtension}";
        }
        catch (Exception ex)
        {
            // Builds/renders never touch email or the network - a failure
            // here is almost always either a bad ?format= value or the
            // Templates/ folder not being present next to the running app
            // (see README "The common Templates folder" - a stale build
            // that predates it is the usual cause). Logged with the full
            // exception so it shows up in the API's own console/log file
            // even outside Development, where UseDeveloperExceptionPage
            // above doesn't put it in the HTTP response.
            _logger.LogError(ex, "Material export failed to build/render (releaseId={ReleaseId}, format={Format}).", id, format);
            throw;
        }

        if (string.IsNullOrWhiteSpace(emailTo))
            return File(bytes, renderer.ContentType, filename);

        try
        {
            var body = await BuildEmailBodyAsync(report, emailTo, ct);
            await _emailSender.SendAsync(
                emailTo,
                $"LOTAM Sentinel — {report.Title}",
                body,
                new List<EmailAttachment> { new(filename, bytes, renderer.ContentType) }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Emailing material export to {EmailTo} failed.", emailTo);
            return StatusCode(500, new GenericMessageDto(
                $"Could not email this report: {ex.Message}"));
        }

        return Ok(new ExportSendResultDto(true, $"Report emailed to {emailTo}."));
    }

    /// <summary>Composes the colorful, templated email body (Templates/Email/
    /// report-notification.html via ReportEmailComposer) - same helper used
    /// by ChangesController.Export, so every export/report email looks and
    /// reads identically regardless of which page it was sent from. Looks up
    /// the recipient's own name if they're a registered LOTAM Sentinel user
    /// (matches on email), otherwise derives a best-effort first name from
    /// the email address itself.</summary>
    private async Task<string> BuildEmailBodyAsync(ExportReport report, string emailTo, CancellationToken ct)
    {
        var recipientUser = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == emailTo.ToLower(), ct);
        var firstName = recipientUser?.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? ReportEmailComposer.DeriveFirstNameFromEmail(emailTo);

        var groupsDisplay = ReportEmailComposer.BuildGroupsDisplay(report);

        return ReportEmailComposer.Compose(new ReportEmailContext(
            RecipientFirstName: firstName,
            ReportTitle: report.Title,
            IntroBullets: new List<string>
            {
                "New suppliers identified in the latest LOTAM update.",
                "Updates to existing suppliers based on the latest available information.",
                "Relevant changes filtered according to the applicable material group(s).",
            },
            RecordCount: report.Rows.Count,
            MaterialGroupsDisplay: groupsDisplay,
            GeneratedAt: DateTimeOffset.UtcNow));
    }
}
