namespace LotamSentinel.Application.Ingestion;

/// <summary>Everything extracted from one LOTAM PDF, before any DB identity
/// (supplier/technology matching) has been resolved.</summary>
public class ParsedRelease
{
    public DateOnly? ReleaseDate { get; set; } // parsed from the printed header, e.g. "27 /07/2025"
    public int PageCount { get; set; }
    public List<ParsedSection> Sections { get; set; } = new();
}

public class ParsedSection
{
    public string CategoryName { get; set; } = null!;   // e.g. "CONDUCTORS, BARE"
    public string Code { get; set; } = null!;            // e.g. "52.2"
    public string RawDescription { get; set; } = null!;
    public string? ApplicableStandard { get; set; }
    public int PrintedTotal { get; set; }
    public int FirstPageNumber { get; set; }
    public int LastPageNumber { get; set; }
    public List<ParsedEntry> Entries { get; set; } = new();
}

public class ParsedEntry
{
    public int OrdinalInSection { get; set; }
    public int PageNumber { get; set; }

    /// <summary>Full column-2 text block, verbatim, newlines preserved.</summary>
    public string RawManufacturerBlock { get; set; } = null!;
    public string? ManufacturerCountryRaw { get; set; }   // best-effort heuristic pull, see PdfLotamParser

    public string? LocalRepresentativeRaw { get; set; }   // column 3 - optional, never inferred if empty
    public string? ModelRatingRaw { get; set; }            // column 4
    public string? TypeTestsRaw { get; set; }              // column 5
    public string? RemarksRaw { get; set; }                // column 6

    /// <summary>Best-effort manufacturer name guess (first non-empty line of the
    /// raw block). Kept separate from RawManufacturerBlock, which is never
    /// altered - this is a derived convenience field for alias creation.</summary>
    public string GuessedManufacturerName { get; set; } = null!;
}
