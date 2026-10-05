using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LotamSentinel.Application.Templating;

namespace LotamSentinel.Application.Reporting;

/// <summary>
/// Renders an ExportReport as a single self-contained, interactive HTML
/// file - searchable, filterable, with collapsible group/record cards - in
/// the same spirit as a hand-built LOTAM browser page, so anyone can open
/// it in any browser (no LOTAM Sentinel login, no network access) and still
/// filter/search/print it. Everything the page needs (styles, script, and
/// the report's own data as embedded JSON) is inlined; nothing is loaded
/// from outside.
///
/// Rows are grouped by ReportRow.GroupCode when the builder set one
/// (Material/Change reports both do, keyed by material section code); a
/// report with no grouping metadata at all renders as a single "All
/// records" group instead of failing.
/// </summary>
public class HtmlReportRenderer : IReportRenderer
{
    public string ContentType => "text/html";
    public string FileExtension => "html";
    public string FormatKey => "html";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keep non-ASCII (Arabic supplier names, etc.) as literal UTF-8
        // rather than \uXXXX-escaped - the page itself declares UTF-8.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public byte[] Render(ExportReport report)
    {
        var data = BuildData(report);
        var json = JsonSerializer.Serialize(data, JsonOptions);
        // Defends against a "</script>" sequence inside any free-text field
        // (a remarks/explanation value, say) prematurely closing the
        // embedding <script> tag.
        json = json.Replace("</", "<\\/");

        var html = BuildHtml(report, json);
        return Encoding.UTF8.GetBytes(html);
    }

    private static RenderData BuildData(ExportReport report)
    {
        var groups = new List<RenderGroup>();
        var groupsByKey = new Dictionary<string, RenderGroup>();

        foreach (var row in report.Rows)
        {
            var key = row.GroupCode ?? "";
            if (!groupsByKey.TryGetValue(key, out var group))
            {
                group = new RenderGroup
                {
                    Code = row.GroupCode ?? "",
                    Label = row.GroupLabel ?? (report.Rows.Count > 0 ? "All records" : ""),
                    Standard = row.GroupStandard,
                };
                groupsByKey[key] = group;
                groups.Add(group);
            }

            group.Rows.Add(new RenderRow
            {
                SupplierName = row.SupplierName ?? "—",
                Country = row.Country,
                Tags = row.Tags.Select(t => new RenderTag { Text = t.Text, ColorTag = t.ColorTag }).ToList(),
                Cells = row.Cells.Select((c, i) => new RenderCell
                {
                    Header = i < report.Columns.Count ? report.Columns[i].Header : $"Column {i + 1}",
                    Text = string.IsNullOrWhiteSpace(c.Text) ? "—" : c.Text,
                    Changed = c.HighlightTag == "changed",
                }).ToList(),
                Explanation = row.Explanation,
            });
        }

        // Numeric-aware sort by group code ("1.1" before "1.10" before
        // "2.1") - same convention used throughout the rest of the app
        // (ReleasesController.ParseSortKey / the frontend's parseSortKey).
        groups.Sort((a, b) => CompareCode(a.Code, b.Code));

        return new RenderData
        {
            Title = report.Title,
            Subtitle = report.Subtitle,
            GeneratedAt = DateTime.UtcNow.ToString("dd MMM yyyy, HH:mm 'UTC'"),
            Columns = report.Columns.Select(c => c.Header).ToList(),
            Groups = groups,
            ExecutiveSummary = report.ExecutiveSummary,
            SummaryStats = report.SummaryStats
                .Select(s => new RenderStat { Label = s.Label, Value = s.Value, ColorTag = s.ColorTag })
                .ToList(),
            ReportSummary = report.ReportSummary,
        };
    }

    private static int CompareCode(string a, string b)
    {
        static (int, int) Key(string code)
        {
            var parts = code.Split('.', 2);
            int.TryParse(parts[0], out var x);
            int.TryParse(parts.Length > 1 ? parts[1] : "0", out var y);
            return (x, y);
        }
        var (a1, a2) = Key(a);
        var (b1, b2) = Key(b);
        return a1 != b1 ? a1.CompareTo(b1) : a2.CompareTo(b2);
    }

    /// <summary>Loads Templates/Reports/report-shell.html + report.css (see
    /// TemplateLoader) and substitutes their three tokens. Both files are
    /// cached in memory after the first read, so this is a cheap
    /// string.Replace on every subsequent export, not a per-request disk
    /// hit.</summary>
    private static string BuildHtml(ExportReport report, string json)
    {
        var shell = TemplateLoader.Load("Reports/report-shell.html");
        var css = TemplateLoader.Load("Reports/report.css");
        return shell
            .Replace("__REPORT_TITLE__", Escape(report.Title))
            .Replace("__REPORT_CSS__", css)
            .Replace("__REPORT_JSON__", json);
    }

    private static string Escape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private class RenderData
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string GeneratedAt { get; set; } = "";
        public List<string> Columns { get; set; } = new();
        public List<RenderGroup> Groups { get; set; } = new();
        public string ExecutiveSummary { get; set; } = "";
        public List<RenderStat> SummaryStats { get; set; } = new();
        public string ReportSummary { get; set; } = "";
    }

    private class RenderStat
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        public string ColorTag { get; set; } = "neutral";
    }

    private class RenderGroup
    {
        public string Code { get; set; } = "";
        public string Label { get; set; } = "";
        public string? Standard { get; set; }
        public List<RenderRow> Rows { get; set; } = new();
    }

    private class RenderRow
    {
        public string SupplierName { get; set; } = "";
        public string? Country { get; set; }
        public List<RenderTag> Tags { get; set; } = new();
        public List<RenderCell> Cells { get; set; } = new();
        public string? Explanation { get; set; }
    }

    private class RenderTag
    {
        public string Text { get; set; } = "";
        public string ColorTag { get; set; } = "neutral";
    }

    private class RenderCell
    {
        public string Header { get; set; } = "";
        public string Text { get; set; } = "";
        public bool Changed { get; set; }
    }
}
