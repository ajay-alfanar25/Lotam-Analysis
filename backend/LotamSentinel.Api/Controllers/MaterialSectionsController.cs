using LotamSentinel.Api.Dtos;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

/// <summary>
/// Backs the Material Explorer page: browse ONE release's data in the
/// document's own structure (material section -> its manufacturer entries),
/// not filtered through our own technology taxonomy. NEW_RECORD/REMOVED_RECORD
/// badges are looked up from change_record - no new comparison logic, this
/// just surfaces what the comparator already computed.
/// </summary>
[ApiController]
[Route("api/material-sections")]
public class MaterialSectionsController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    public MaterialSectionsController(LotamSentinelDbContext db) => _db = db;

    [HttpGet("{id:long}")]
    public async Task<ActionResult<MaterialSectionDetailDto>> GetDetail(
        long id, [FromQuery] long? previousReleaseId, [FromQuery] long? currentReleaseId)
    {
        var section = await _db.MaterialSections
            .Include(m => m.MaterialCategory)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (section is null) return NotFound();

        var entries = await _db.LotamEntries
            .Where(e => e.MaterialSectionId == id)
            .OrderBy(e => e.OrdinalInSection)
            .ToListAsync();

        // Badge lookup only makes sense if the caller told us which release
        // pair is currently selected (the top-bar previous/current picker).
        // Without both ids we simply show no badges - browsing a single
        // release in isolation, no comparison context.
        HashSet<long> newEntryIds = new();
        HashSet<long> removedEntryIds = new();
        HashSet<long> updatedEntryIds = new();

        // Any change type other than New/Removed means this entry exists in
        // BOTH releases but something about it changed (status, remarks,
        // rating, agent, country, etc.) - "Updated", not "Removed". This is
        // exactly the case your STATCOM/NR Electric screenshot caught: once
        // the parser correctly extracts the same manufacturer name in both
        // releases, supplier identity matching pairs the two entries instead
        // of seeing them as unrelated (which is what produced the incorrect
        // "Removed" badge before).
        var updateChangeTypes = new[]
        {
            ChangeType.StatusChanged, ChangeType.ModelAdded, ChangeType.ModelRemoved,
            ChangeType.RatingChanged, ChangeType.VoltageChanged, ChangeType.SpecificationChanged,
            ChangeType.ConditionChanged, ChangeType.RemarkChanged, ChangeType.AgentChanged,
            ChangeType.CountryChanged, ChangeType.ScopeChanged, ChangeType.OtherMaterialChange
        };

        if (previousReleaseId is not null && currentReleaseId is not null)
        {
            if (section.ReleaseId == currentReleaseId)
            {
                var pairChanges = await _db.ChangeRecords
                    .Where(c => c.PreviousReleaseId == previousReleaseId && c.CurrentReleaseId == currentReleaseId
                             && c.CurrentEntryId != null)
                    .ToListAsync();

                newEntryIds = pairChanges.Where(c => c.ChangeType == ChangeType.NewRecord)
                    .Select(c => c.CurrentEntryId!.Value).ToHashSet();
                updatedEntryIds = pairChanges.Where(c => updateChangeTypes.Contains(c.ChangeType))
                    .Select(c => c.CurrentEntryId!.Value).ToHashSet();
            }
            else if (section.ReleaseId == previousReleaseId)
            {
                var pairChanges = await _db.ChangeRecords
                    .Where(c => c.PreviousReleaseId == previousReleaseId && c.CurrentReleaseId == currentReleaseId
                             && c.PreviousEntryId != null)
                    .ToListAsync();

                removedEntryIds = pairChanges.Where(c => c.ChangeType == ChangeType.RemovedRecord)
                    .Select(c => c.PreviousEntryId!.Value).ToHashSet();
                updatedEntryIds = pairChanges.Where(c => updateChangeTypes.Contains(c.ChangeType))
                    .Select(c => c.PreviousEntryId!.Value).ToHashSet();
            }
        }

        var entryDtos = entries.Select(e => new MaterialSectionEntryDto(
            e.Id,
            e.RawManufacturerBlock,
            e.LocalRepresentativeRaw,
            e.ModelRatingRaw,
            e.TypeTestsRaw,
            e.RemarksRaw,
            e.DerivedStatus.ToString(),
            newEntryIds.Contains(e.Id) ? "New"
                : removedEntryIds.Contains(e.Id) ? "Removed"
                : updatedEntryIds.Contains(e.Id) ? "Updated"
                : null
        )).ToList();

        return Ok(new MaterialSectionDetailDto(
            section.Id, section.Code, section.MaterialCategory.Name, section.RawDescription,
            section.ApplicableStandard, section.PrintedTotal, section.ValidationStatus.ToString(),
            entryDtos));
    }
}
