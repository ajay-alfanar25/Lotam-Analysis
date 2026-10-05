using LotamSentinel.Domain.Entities;

namespace LotamSentinel.Api.Dtos;

public static class ChangeMapper
{
    public static ChangeRecordDto ToDto(ChangeRecord c, IReadOnlyList<string>? technologyCodes = null)
        => new(
            c.Id,
            c.Supplier?.CanonicalName ?? "(unmatched supplier)",
            c.MaterialSection?.Code,
            c.MaterialSection?.RawDescription,
            c.ChangeType.ToString(),
            c.PreviousValue,
            c.CurrentValue,
            c.Severity.ToString(),
            c.Explanation,
            c.PreviousRelease?.ReleaseDate ?? default,
            c.CurrentRelease?.ReleaseDate ?? default,
            technologyCodes?.ToList() ?? new List<string>());
}
