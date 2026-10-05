using LotamSentinel.Api.Dtos;
using LotamSentinel.Application.Comparison;
using LotamSentinel.Application.Email;
using LotamSentinel.Application.Reporting;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

public record PagedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize);

[ApiController]
[Route("api/changes")]
public class ChangesController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    private readonly ChangeReportBuilder _reportBuilder;
    private readonly IReportRendererFactory _rendererFactory;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<ChangesController> _logger;

    public ChangesController(
        LotamSentinelDbContext db, ChangeReportBuilder reportBuilder,
        IReportRendererFactory rendererFactory, IEmailSender emailSender, ILogger<ChangesController> logger)
    {
        _db = db;
        _reportBuilder = reportBuilder;
        _rendererFactory = rendererFactory;
        _emailSender = emailSender;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ChangeRecordDto>>> GetChanges(
        [FromQuery] long previousReleaseId,
        [FromQuery] long currentReleaseId,
        [FromQuery] string? technology,
        [FromQuery] string? supplier,
        [FromQuery] string? severity,
        [FromQuery] string? changeType,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var query = _db.ChangeRecords
            .Include(c => c.Supplier)
            .Include(c => c.MaterialSection)
            .Include(c => c.PreviousRelease)
            .Include(c => c.CurrentRelease)
            .Where(c => c.PreviousReleaseId == previousReleaseId && c.CurrentReleaseId == currentReleaseId);

        if (!string.IsNullOrWhiteSpace(supplier))
            query = query.Where(c => c.Supplier != null && EF.Functions.ILike(c.Supplier.CanonicalName, $"%{supplier}%"));

        if (!string.IsNullOrWhiteSpace(severity) && Enum.TryParse<Severity>(severity, true, out var sev))
            query = query.Where(c => c.Severity == sev);

        if (!string.IsNullOrWhiteSpace(changeType) && Enum.TryParse<ChangeType>(changeType, true, out var ct))
            query = query.Where(c => c.ChangeType == ct);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c =>
                EF.Functions.ILike(c.Explanation, $"%{search}%") ||
                (c.Supplier != null && EF.Functions.ILike(c.Supplier.CanonicalName, $"%{search}%")) ||
                (c.MaterialSection != null && EF.Functions.ILike(c.MaterialSection.RawDescription, $"%{search}%")));

        if (!string.IsNullOrWhiteSpace(technology))
        {
            var sectionIds = await _db.MaterialSectionTechnologies
                .Include(mst => mst.Technology)
                .Where(mst => mst.Technology.Code == technology)
                .Select(mst => mst.MaterialSectionId)
                .ToListAsync();
            query = query.Where(c => c.MaterialSectionId != null && sectionIds.Contains(c.MaterialSectionId!.Value));
        }

        var totalCount = await query.CountAsync();
        var entities = await query
            .OrderByDescending(c => c.Severity == Severity.Critical)
            .ThenByDescending(c => c.Severity == Severity.High)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var sectionIdsForPage = entities.Where(e => e.MaterialSectionId != null)
            .Select(e => e.MaterialSectionId!.Value).Distinct().ToList();
        var techLookup = await _db.MaterialSectionTechnologies
            .Include(mst => mst.Technology)
            .Where(mst => sectionIdsForPage.Contains(mst.MaterialSectionId))
            .ToListAsync();
        var techBySection = techLookup.GroupBy(t => t.MaterialSectionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Technology.Code).ToList());

        var items = entities.Select(c => ChangeMapper.ToDto(
            c, c.MaterialSectionId.HasValue && techBySection.TryGetValue(c.MaterialSectionId.Value, out var codes)
                ? codes : null)).ToList();

        return Ok(new PagedResult<ChangeRecordDto>(items, totalCount, page, pageSize));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ChangeRecordDetailDto>> GetDetail(long id)
    {
        var c = await _db.ChangeRecords
            .Include(x => x.Supplier)
            .Include(x => x.MaterialSection)
            .Include(x => x.PreviousRelease)
            .Include(x => x.CurrentRelease)
            .Include(x => x.PreviousEntry)
            .Include(x => x.CurrentEntry)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (c is null) return NotFound();

        var dto = ChangeMapper.ToDto(c);
        var detail = new ChangeRecordDetailDto(
            dto,
            c.PreviousEntry?.PageNumber is { } pp ? $"Page {pp} ({c.PreviousRelease.SourceFilename})" : null,
            c.CurrentEntry?.PageNumber is { } cp ? $"Page {cp} ({c.CurrentRelease.SourceFilename})" : null,
            c.PreviousEntry?.RemarksRaw ?? c.PreviousEntry?.RawManufacturerBlock,
            c.CurrentEntry?.RemarksRaw ?? c.CurrentEntry?.RawManufacturerBlock);

        return Ok(detail);
    }

    /// <summary>Generates the change report on the fly and streams it
    /// directly in the response - nothing is written to disk, and no copy
    /// of it persists anywhere server-side once the request completes.
    /// Same filter parameters as GET above, just unpaginated.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] long previousReleaseId, [FromQuery] long currentReleaseId,
        [FromQuery] string? technology, [FromQuery] string? supplier, [FromQuery] string? severity,
        [FromQuery] string? changeType, [FromQuery] string? search,
        [FromQuery] string? format, [FromQuery] string? emailTo, CancellationToken ct)
    {
        ExportReport report;
        IReportRenderer renderer;
        byte[] bytes;
        string filename;
        try
        {
            report = await _reportBuilder.BuildAsync(
                previousReleaseId, currentReleaseId, technology, supplier, severity, changeType, search, ct);
            renderer = _rendererFactory.Get(format);
            bytes = renderer.Render(report);
            filename = $"LOTAM-Sentinel-Changes-{DateTime.UtcNow:yyyy-MM-dd}.{renderer.FileExtension}";
        }
        catch (Exception ex)
        {
            // Same reasoning as ReleasesController.ExportEntries - this step
            // never touches email/network, so a failure here is a bad
            // ?format= value or a missing Templates/ folder next to the
            // running app, not an SMTP/connectivity issue.
            _logger.LogError(ex, "Change export failed to build/render (previousReleaseId={PreviousReleaseId}, currentReleaseId={CurrentReleaseId}, format={Format}).",
                previousReleaseId, currentReleaseId, format);
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
            _logger.LogError(ex, "Emailing change export to {EmailTo} failed.", emailTo);
            return StatusCode(500, new GenericMessageDto(
                $"Could not email this report: {ex.Message}"));
        }

        return Ok(new ExportSendResultDto(true, $"Report emailed to {emailTo}."));
    }

    /// <summary>Same templated-email composition as
    /// ReleasesController.BuildEmailBodyAsync (see there) - bullets are
    /// phrased for a change report rather than a material report, everything
    /// else is identical so both exports read as one consistent product.</summary>
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
                "New supplier qualifications and status changes since the previous release.",
                "Updates to existing supplier approvals, models, and ratings.",
                "Changes filtered according to the selected technology, severity, or change type.",
            },
            RecordCount: report.Rows.Count,
            MaterialGroupsDisplay: groupsDisplay,
            GeneratedAt: DateTimeOffset.UtcNow));
    }
}
