namespace LotamSentinel.Application.Reporting;

/// <summary>
/// One column definition. WidthWeight is a relative sizing hint (e.g. a
/// column weighted 3 gets roughly 3x the width of one weighted 1) - a
/// renderer is free to ignore it (an HTML table wouldn't need it at all).
/// </summary>
public record ReportColumn(string Header, float WidthWeight = 1f);

/// <summary>One cell's text, plus an optional semantic highlight tag. The
/// tag is a plain string ("changed") rather than a color, so a renderer
/// decides what that means in its own format - a future HTML renderer
/// might use a CSS class instead of an inline color.</summary>
public record ReportCell(string Text, string? HighlightTag = null);

/// <summary>A small labeled tag shown alongside a row (status, New/Updated/
/// Removed badge, etc.) - ColorTag is semantic ("emerald", "rose", "amber",
/// "neutral"), not a literal color, for the same reason as ReportCell.</summary>
public record ReportTag(string Text, string ColorTag = "neutral");

public class ReportRow
{
    public List<ReportCell> Cells { get; set; } = new();
    public List<ReportTag> Tags { get; set; } = new();

    // ---- Optional grouping/identity metadata ----
    // Both renderers group rows into per-material-section tables/cards
    // using GroupCode/GroupLabel/GroupStandard (SupplierName/Country are
    // used by HtmlReportRenderer for each card's header; PdfReportRenderer
    // reads SupplierName/Country too, for its OEM/Supplier Status
    // Directory table). Left null by a builder that has no natural
    // grouping; both renderers then fall back to one flat "All records"
    // group.
    public string? GroupCode { get; set; }
    public string? GroupLabel { get; set; }
    public string? GroupStandard { get; set; }
    public string? SupplierName { get; set; }
    public string? Country { get; set; }

    // ---- Optional one-line AI explanation ----
    // Set by ChangeReportBuilder from ChangeRecord.Explanation (the same
    // AI-generated sentence shown on the Change Explorer page). Null for
    // MaterialReportBuilder rows, which have no per-record "what changed"
    // narrative. Both HtmlReportRenderer and PdfReportRenderer surface it
    // as a highlighted one-line summary at the top of the record's
    // card/cell when present.
    public string? Explanation { get; set; }
}

/// <summary>One headline number in the executive summary panel/box - e.g.
/// ("Suppliers", "12"), ("New", "2", "emerald"). ColorTag follows the same
/// semantic convention as ReportTag.ColorTag.</summary>
public record SummaryStat(string Label, string Value, string ColorTag = "neutral");

public class ExportReport
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public List<ReportColumn> Columns { get; set; } = new();
    public List<ReportRow> Rows { get; set; } = new();

    // ---- Executive summary (both set by the builder, both optional) ----
    // Rendered as a distinct panel just before the search box in the HTML
    // export, and as a highlighted box near the top of the PDF export -
    // the headline numbers an executive skimming the report needs without
    // reading every row. Left empty/unset, both renderers simply omit the
    // section rather than showing an empty box.
    public string ExecutiveSummary { get; set; } = "";
    public List<SummaryStat> SummaryStats { get; set; } = new();

    // ---- Report Summary (set by the builder via ReportSummaryText.Build) ----
    // The closing "Report Summary" section in both exports - what this
    // specific report is actually about (the named material group(s) when
    // there are few enough to name, otherwise alfanar's generic technology-
    // area list - see ReportSummaryText below). Deliberately rendered at the
    // END of the report in both HTML and PDF, not the top, per feedback
    // that a boilerplate "about" blurb shouldn't be the first thing a
    // reader sees.
    public string ReportSummary { get; set; } = "";
}

/// <summary>Builds the "Report Summary" paragraph shared by both exports -
/// see ExportReport.ReportSummary. Computed from the distinct
/// ReportRow.GroupLabel values the export actually ended up covering, so
/// the sentence names the real material group(s) this particular report is
/// about (e.g. a Change Explorer export filtered to one technology) instead
/// of a one-size-fits-all blurb every time. Falls back to alfanar's generic
/// technology-area list when there are too many distinct groups to name
/// without the sentence turning into a dump of the group list, or none at
/// all (an empty report).</summary>
public static class ReportSummaryText
{
    private const int MaxNamedGroups = 3;

    public static string Build(IEnumerable<string?> groupLabels)
    {
        var distinctLabels = groupLabels
            .Where(l => !string.IsNullOrWhiteSpace(l) && l != "Ungrouped")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var scope = distinctLabels.Count is >= 1 and <= MaxNamedGroups
            ? $"of {JoinWithAnd(distinctLabels.Select(l => $"\"{l}\""))}"
            : "across alfanar's Medium Voltage, Low Voltage, DASA, Substation Automation, STATCOM, HVDC, " +
              "DC Systems and Renewable Energy technology areas";

        return "This report is generated automatically by LOTAM Sentinel, alfanar's SEC LOTAM intelligence " +
               "platform. It compares the latest published SEC List of Technically Approved Materials (LOTAM) " +
               $"against previous releases and tracks suppliers, technologies, materials and approval-status " +
               $"changes {scope}, including the full approval status of every OEM covered below.";
    }

    private static string JoinWithAnd(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1],
        };
    }
}

/// <summary>Turns a format-agnostic ExportReport into actual file bytes.
/// PdfReportRenderer and HtmlReportRenderer both implement this; a
/// controller resolves the right one at request time via
/// IReportRendererFactory rather than depending on either directly, so
/// ChangeReportBuilder/MaterialReportBuilder never need to know or care
/// which format the caller asked for.</summary>
public interface IReportRenderer
{
    byte[] Render(ExportReport report);
    string ContentType { get; }
    string FileExtension { get; }

    /// <summary>Short lowercase key used to select this renderer from a
    /// request's ?format= query parameter - "pdf", "html".</summary>
    string FormatKey { get; }
}
