using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace LotamSentinel.Application.Reporting;

/// <summary>
/// Renders an ExportReport as a landscape A4 PDF, streamed to bytes -
/// nothing is ever written to disk.
///
/// "Print Bulletin" theme - the same alfanar-branded design as the HTML
/// export's report-shell.html (a masthead carrying the alfanar logo,
/// numbered sections, a full OEM/supplier status directory with rows
/// tinted by approval status, and per-record status distribution), kept in
/// visual sync so a report looks the same whether it's opened as an
/// interactive HTML page or a printed/emailed PDF. The two renderers stay
/// independent implementations rather than sharing layout code (MigraDoc's
/// table/paragraph model and HTML/CSS have nothing in common to share),
/// but they read the exact same format-agnostic ExportReport, so neither
/// one needs to know whether its data came from Change Explorer or
/// Material Explorer.
///
/// One deliberate difference from the HTML version: the HTML export's
/// donut/ring chart has no equivalent in MigraDoc (a text/table layout
/// engine, not a drawing canvas), so the PDF shows the same status
/// distribution as a proportional stacked bar + colored-text legend
/// instead - same information, a chart MigraDoc can actually render
/// reliably in every PDF viewer.
/// </summary>
public class PdfReportRenderer : IReportRenderer
{
    // PDFsharp 6.x on .NET (non-Framework) has no font resolver registered
    // by default and throws on the very first font it needs - see
    // LotamPdfFontResolver.cs for the full explanation. A static
    // constructor runs exactly once (CLR-guaranteed, thread-safe) before
    // this class is used for the first time, regardless of how many times
    // Render() is called or from how many requests.
    private static readonly string LogoImagePath;

    static PdfReportRenderer()
    {
        GlobalFontSettings.FontResolver ??= new LotamPdfFontResolver();
        LogoImagePath = "base64:" + Convert.ToBase64String(LoadEmbeddedLogo());
    }

    private static byte[] LoadEmbeddedLogo()
    {
        const string resourceName = "LotamSentinel.Application.Reporting.Assets.alfanar-logo.png";
        using var stream = typeof(PdfReportRenderer).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded logo resource not found: {resourceName}. Check the EmbeddedResource entry in LotamSentinel.Application.csproj.");
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    public string ContentType => "application/pdf";
    public string FileExtension => "pdf";
    public string FormatKey => "pdf";

    // Same alfanar brand blue used throughout the HTML export (picked by
    // pixel-sampling the real logo asset) plus the same emerald/rose/amber/
    // neutral status-color convention as HtmlReportRenderer/report.css.
    private static readonly Color Brand = Color.FromRgb(11, 72, 157);
    private static readonly Color BrandDark = Color.FromRgb(8, 49, 110);
    private static readonly Color BrandLight = Color.FromRgb(234, 241, 251);
    private static readonly Color BorderGray = Color.FromRgb(215, 222, 232);
    private static readonly Color MutedGray = Color.FromRgb(91, 100, 116);
    private static readonly Color TextDark = Color.FromRgb(24, 28, 36);
    private static readonly Color RowEven = Color.FromRgb(255, 255, 255);
    private static readonly Color RowOdd = Color.FromRgb(247, 249, 251);
    private static readonly Color HighlightChanged = Color.FromRgb(250, 232, 189);

    private static readonly Dictionary<string, Color> TagColors = new()
    {
        ["emerald"] = Color.FromRgb(26, 122, 68),
        ["rose"] = Color.FromRgb(161, 26, 38),
        ["amber"] = Color.FromRgb(138, 90, 0),
        ["neutral"] = Color.FromRgb(91, 100, 116),
    };

    private static readonly Dictionary<string, Color> TagBgColors = new()
    {
        ["emerald"] = Color.FromRgb(230, 244, 236),
        ["rose"] = Color.FromRgb(249, 231, 232),
        ["amber"] = Color.FromRgb(249, 240, 224),
        ["neutral"] = Color.FromRgb(236, 238, 241),
    };

    public byte[] Render(ExportReport report)
    {
        var document = new Document();
        document.Info.Title = report.Title;

        var section = document.AddSection();
        section.PageSetup.Orientation = Orientation.Landscape;
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1);

        const float usableWidthCm = 26.7f;

        AddFooter(section);
        AddMasthead(section, usableWidthCm);
        AddTitleBlock(section, report);
        AddReportSummary(section, report);
        AddExecutiveSummary(section, report, usableWidthCm);
        AddOemDirectory(section, report, usableWidthCm);
        AddDetailedRecords(section, report, usableWidthCm);

        using var stream = new MemoryStream();
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static void AddFooter(Section section)
    {
        var footer = section.Footers.Primary.AddParagraph("LOTAM Sentinel — Confidential, for internal use only · alfanar");
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = MutedGray;
        footer.Format.Alignment = ParagraphAlignment.Center;
    }

    /// <summary>Brand-blue banner spanning the page width, with the report
    /// name on the left and the alfanar logo on the right - the PDF's
    /// equivalent of report-shell.html's <c>.masthead</c>.</summary>
    private static void AddMasthead(Section section, float usableWidthCm)
    {
        var table = section.AddTable();
        table.Borders.Width = 0;
        table.AddColumn(Unit.FromCentimeter(usableWidthCm - 4));
        table.AddColumn(Unit.FromCentimeter(4));

        var row = table.AddRow();
        row.Shading.Color = Brand;
        row.TopPadding = Unit.FromCentimeter(0.3);
        row.BottomPadding = Unit.FromCentimeter(0.3);
        row.Cells[0].VerticalAlignment = VerticalAlignment.Center;
        row.Cells[1].VerticalAlignment = VerticalAlignment.Center;

        var name = row.Cells[0].AddParagraph("LOTAM Sentinel Bulletin");
        name.Format.Font.Size = 15;
        name.Format.Font.Bold = true;
        name.Format.Font.Color = Colors.White;
        name.Format.LeftIndent = Unit.FromCentimeter(0.3);

        var sub = row.Cells[0].AddParagraph("ALFANAR REGULATORY INTELLIGENCE");
        sub.Format.Font.Size = 8;
        sub.Format.Font.Color = Color.FromRgb(210, 224, 245);
        sub.Format.LeftIndent = Unit.FromCentimeter(0.3);

        row.Cells[1].Shading.Color = Brand;
        row.Cells[1].Borders.Right.Width = 0;
        var logoCellRow = row.Cells[1];
        // A small white rounded backing behind the logo mirrors the HTML
        // masthead's white logo chip on a blue background.
        var logoImage = logoCellRow.AddImage(LogoImagePath);
        logoImage.Height = Unit.FromCentimeter(1.1);
        logoImage.LockAspectRatio = true;
        logoCellRow.Format.Alignment = ParagraphAlignment.Right;

        var spacer = section.AddParagraph();
        spacer.Format.SpaceAfter = Unit.FromCentimeter(0.15);
    }

    private static void AddTitleBlock(Section section, ExportReport report)
    {
        var title = section.AddParagraph(report.Title);
        title.Format.Font.Size = 16;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = BrandDark;
        title.Format.SpaceAfter = Unit.FromCentimeter(0.1);

        if (!string.IsNullOrWhiteSpace(report.Subtitle))
        {
            var subtitle = section.AddParagraph(report.Subtitle);
            subtitle.Format.Font.Size = 8.5;
            subtitle.Format.Font.Color = MutedGray;
            subtitle.Format.SpaceAfter = Unit.FromCentimeter(0.3);
        }
    }

    /// <summary>Opening "Report Summary" paragraph, right under the title -
    /// the PDF's equivalent of report-shell.html's top "Report Summary" box
    /// (formerly labeled "About this report"). Text comes straight from
    /// ExportReport.ReportSummary (built by ReportSummaryText.Build from
    /// the report's own rows), so the PDF and HTML exports read identically
    /// and both name the actual material group(s)/scope this report covers
    /// rather than a generic blurb.</summary>
    private static void AddReportSummary(Section section, ExportReport report)
    {
        var summary = section.AddParagraph();
        summary.Format.Borders.Top.Width = 0.75;
        summary.Format.Borders.Top.Color = BorderGray;
        summary.Format.SpaceBefore = Unit.FromCentimeter(0.2);
        summary.Format.SpaceAfter = Unit.FromCentimeter(0.35);
        summary.Format.Font.Size = 8.5;
        summary.Format.Font.Italic = true;
        summary.Format.Font.Color = TextDark;

        var label = summary.AddFormattedText("Report Summary.  ", TextFormat.Bold);
        label.Font.Italic = false;
        label.Font.Color = BrandDark;
        summary.AddText(string.IsNullOrWhiteSpace(report.ReportSummary) ? "" : report.ReportSummary);
    }

    private static void AddSectionHeading(Section section, string number, string titleText)
    {
        var heading = section.AddParagraph();
        heading.Format.SpaceBefore = Unit.FromCentimeter(0.3);
        heading.Format.SpaceAfter = Unit.FromCentimeter(0.15);
        heading.Format.Borders.Bottom.Width = 1.4;
        heading.Format.Borders.Bottom.Color = Brand;
        heading.Format.Font.Size = 12.5;
        heading.Format.Font.Bold = true;
        heading.Format.Font.Color = BrandDark;

        // MigraDoc has no inline-shape "badge" primitive and can't shade an
        // individual run's background, so the section number is rendered as
        // plain bold brand-colored text ("1  Executive Summary") instead of
        // the HTML export's circular chip - same information either way.
        var numberRun = heading.AddFormattedText($"{number}   ", TextFormat.Bold);
        numberRun.Font.Color = Brand;
        heading.AddText(titleText);
    }

    /// <summary>Headline stat tiles + a status-distribution bar + narrative,
    /// all rebuilt from the same format-agnostic ExportReport fields the
    /// HTML export's Executive Summary panel uses (SummaryStats /
    /// ExecutiveSummary / row Tags) - never re-derived differently, so the
    /// two exports can't show different numbers for the same report.</summary>
    private static void AddExecutiveSummary(Section section, ExportReport report, float usableWidthCm)
    {
        AddSectionHeading(section, "1", "Executive Summary");

        if (report.SummaryStats.Count > 0)
        {
            var statTable = section.AddTable();
            statTable.Borders.Width = 0.75;
            statTable.Borders.Color = BorderGray;
            var colWidth = usableWidthCm / report.SummaryStats.Count;
            foreach (var _ in report.SummaryStats)
                statTable.AddColumn(Unit.FromCentimeter(colWidth));

            var row = statTable.AddRow();
            row.TopPadding = Unit.FromCentimeter(0.2);
            row.BottomPadding = Unit.FromCentimeter(0.2);
            for (int i = 0; i < report.SummaryStats.Count; i++)
            {
                var stat = report.SummaryStats[i];
                var cell = row.Cells[i];
                cell.Borders.Right.Width = i < report.SummaryStats.Count - 1 ? 0.75 : 0;
                cell.Borders.Right.Color = BorderGray;
                cell.Format.Alignment = ParagraphAlignment.Center;

                var value = cell.AddParagraph(stat.Value);
                value.Format.Font.Size = 15;
                value.Format.Font.Bold = true;
                value.Format.Font.Color = TagColors.GetValueOrDefault(stat.ColorTag, TextDark);
                value.Format.Alignment = ParagraphAlignment.Center;

                var label = cell.AddParagraph(stat.Label.ToUpperInvariant());
                label.Format.Font.Size = 6.5;
                label.Format.Font.Color = MutedGray;
                label.Format.Alignment = ParagraphAlignment.Center;
            }

            var spacer = section.AddParagraph();
            spacer.Format.SpaceAfter = Unit.FromCentimeter(0.25);
        }

        AddStatusDistribution(section, report, usableWidthCm);

        if (!string.IsNullOrWhiteSpace(report.ExecutiveSummary))
        {
            var narrative = section.AddParagraph(report.ExecutiveSummary);
            narrative.Format.Font.Size = 9;
            narrative.Format.Font.Color = TextDark;
            narrative.Format.SpaceAfter = Unit.FromCentimeter(0.1);
        }
    }

    /// <summary>Same status-distribution information as the HTML export's
    /// donut ring, shown as a proportional stacked bar + a colored-text
    /// legend instead - MigraDoc lays out text and tables, not arbitrary
    /// vector shapes, so a bar built from proportionally-sized table
    /// columns is the reliable equivalent every PDF viewer renders
    /// identically, rather than something drawn free-hand onto the
    /// page.</summary>
    private static void AddStatusDistribution(Section section, ExportReport report, float usableWidthCm)
    {
        var firstTags = report.Rows.Select(r => r.Tags.FirstOrDefault()?.ColorTag ?? "neutral").ToList();
        var total = firstTags.Count;
        if (total == 0) return;

        var counts = new Dictionary<string, int>
        {
            ["emerald"] = firstTags.Count(t => t == "emerald"),
            ["amber"] = firstTags.Count(t => t == "amber"),
            ["rose"] = firstTags.Count(t => t == "rose"),
        };
        counts["neutral"] = total - counts["emerald"] - counts["amber"] - counts["rose"];

        var segments = new (string Key, string Label, Color Color)[]
        {
            ("emerald", "Positive (Active / New)", TagColors["emerald"]),
            ("amber", "Caution (Conditional / High)", TagColors["amber"]),
            ("rose", "Critical (Suspended / Blacklisted)", TagColors["rose"]),
            ("neutral", "Other", TagColors["neutral"]),
        };
        var nonZero = segments.Where(s => counts[s.Key] > 0).ToList();
        if (nonZero.Count == 0) return;

        var barTable = section.AddTable();
        barTable.Borders.Width = 0;
        foreach (var seg in nonZero)
        {
            var widthCm = Math.Max(0.3, usableWidthCm * counts[seg.Key] / (float)total);
            barTable.AddColumn(Unit.FromCentimeter(widthCm));
        }
        var barRow = barTable.AddRow();
        barRow.Height = Unit.FromCentimeter(0.5);
        barRow.HeightRule = RowHeightRule.Exactly;
        for (int i = 0; i < nonZero.Count; i++)
        {
            barRow.Cells[i].Shading.Color = nonZero[i].Color;
            // Every cell needs at least one element for MigraDoc to render
            // it cleanly - this bar has no text, just a color block.
            barRow.Cells[i].AddParagraph();
        }

        var legend = section.AddParagraph();
        legend.Format.SpaceBefore = Unit.FromCentimeter(0.15);
        legend.Format.SpaceAfter = Unit.FromCentimeter(0.25);
        for (int i = 0; i < nonZero.Count; i++)
        {
            var seg = nonZero[i];
            var run = legend.AddFormattedText($"{seg.Label}: {counts[seg.Key]}", TextFormat.Bold);
            run.Font.Size = 8.5;
            run.Font.Color = seg.Color;
            if (i < nonZero.Count - 1)
                legend.AddText("      ");
        }
    }

    /// <summary>Full OEM/supplier directory, deduplicated across every
    /// group in the report the same way the HTML export's client-side JS
    /// builds it (last occurrence wins as rows are walked in order, then
    /// sorted alphabetically) so both formats list identical suppliers with
    /// identical statuses. Each row's full background is tinted by status
    /// color, matching the HTML directory design the user picked.</summary>
    private static void AddOemDirectory(Section section, ExportReport report, float usableWidthCm)
    {
        AddSectionHeading(section, "2", "Full OEM / Supplier Status Directory");

        var byName = new Dictionary<string, (string? Country, ReportTag? Tag)>();
        foreach (var row in report.Rows)
            byName[row.SupplierName ?? "—"] = (row.Country, row.Tags.FirstOrDefault());

        if (byName.Count == 0)
        {
            var empty = section.AddParagraph("No records in this report.");
            empty.Format.Font.Size = 9;
            empty.Format.Font.Color = MutedGray;
            empty.Format.SpaceAfter = Unit.FromCentimeter(0.3);
            return;
        }

        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = BorderGray;
        table.AddColumn(Unit.FromCentimeter(usableWidthCm * 0.5f));
        table.AddColumn(Unit.FromCentimeter(usableWidthCm * 0.25f));
        table.AddColumn(Unit.FromCentimeter(usableWidthCm * 0.25f));

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Shading.Color = BrandLight;
        header.Format.Font.Bold = true;
        header.Format.Font.Size = 7.5;
        header.Format.Font.Color = BrandDark;
        header.Cells[0].AddParagraph("SUPPLIER");
        header.Cells[1].AddParagraph("COUNTRY");
        header.Cells[2].AddParagraph("STATUS");

        foreach (var name in byName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var (country, tag) = byName[name];
            var colorTag = tag?.ColorTag ?? "neutral";
            var row = table.AddRow();
            row.Shading.Color = TagBgColors.GetValueOrDefault(colorTag, TagBgColors["neutral"]);
            row.Format.Font.Size = 8;
            row.Cells[0].Borders.Left.Width = 2;
            row.Cells[0].Borders.Left.Color = TagColors.GetValueOrDefault(colorTag, TagColors["neutral"]);
            row.Cells[0].AddParagraph(name);
            row.Cells[1].AddParagraph(country ?? "");
            var statusPara = row.Cells[2].AddParagraph(tag?.Text ?? "Unknown");
            statusPara.Format.Font.Bold = true;
            statusPara.Format.Font.Color = TagColors.GetValueOrDefault(colorTag, TagColors["neutral"]);
        }

        var spacer = section.AddParagraph();
        spacer.Format.SpaceAfter = Unit.FromCentimeter(0.1);
    }

    /// <summary>The actual record table(s), one per material group - same
    /// grouping/sort convention as HtmlReportRenderer (numeric-aware by
    /// group code, e.g. "1.2" before "1.10") so a group appears in the same
    /// order in both exports. A report with no grouping metadata at all
    /// (GroupCode never set) renders as one "All records" group instead of
    /// failing, matching HtmlReportRenderer's fallback.</summary>
    private static void AddDetailedRecords(Section section, ExportReport report, float usableWidthCm)
    {
        AddSectionHeading(section, "3", "Detailed Records by Material Group");

        var groups = new List<(string Code, string Label, string? Standard, List<ReportRow> Rows)>();
        var byKey = new Dictionary<string, int>();
        foreach (var row in report.Rows)
        {
            var code = row.GroupCode ?? "";
            if (!byKey.TryGetValue(code, out var idx))
            {
                idx = groups.Count;
                byKey[code] = idx;
                groups.Add((code, row.GroupLabel ?? (report.Rows.Count > 0 ? "All records" : ""), row.GroupStandard, new List<ReportRow>()));
            }
            groups[idx].Rows.Add(row);
        }
        groups.Sort((a, b) => CompareGroupCode(a.Code, b.Code));

        var totalWeight = report.Columns.Sum(c => c.WidthWeight);

        foreach (var group in groups)
        {
            var groupHeading = section.AddParagraph();
            groupHeading.Format.SpaceBefore = Unit.FromCentimeter(0.25);
            groupHeading.Format.SpaceAfter = Unit.FromCentimeter(0.08);

            if (!string.IsNullOrWhiteSpace(group.Code))
            {
                var codeRun = groupHeading.AddFormattedText($" {group.Code} ", TextFormat.Bold);
                codeRun.Font.Size = 8;
                codeRun.Font.Color = BrandDark;
                groupHeading.AddText("  ");
            }
            var labelRun = groupHeading.AddFormattedText(group.Label, TextFormat.Bold);
            labelRun.Font.Size = 10.5;
            labelRun.Font.Color = TextDark;
            if (!string.IsNullOrWhiteSpace(group.Standard))
            {
                groupHeading.AddText("   ");
                var stdRun = groupHeading.AddFormattedText($"Applicable Standards: {group.Standard}");
                stdRun.Font.Size = 8;
                stdRun.Font.Color = MutedGray;
            }

            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = BorderGray;
            foreach (var col in report.Columns)
            {
                var widthCm = totalWeight > 0 ? usableWidthCm * (col.WidthWeight / totalWeight) : usableWidthCm / report.Columns.Count;
                table.AddColumn(Unit.FromCentimeter(widthCm));
            }

            var headerRow = table.AddRow();
            headerRow.HeadingFormat = true;
            headerRow.Shading.Color = Brand;
            headerRow.Format.Font.Color = Colors.White;
            headerRow.Format.Font.Bold = true;
            headerRow.Format.Font.Size = 7.5;
            for (int i = 0; i < report.Columns.Count; i++)
                headerRow.Cells[i].AddParagraph(report.Columns[i].Header);

            for (int r = 0; r < group.Rows.Count; r++)
            {
                var reportRow = group.Rows[r];
                var row = table.AddRow();
                row.Shading.Color = r % 2 == 0 ? RowEven : RowOdd;
                row.Format.Font.Size = 8;
                row.VerticalAlignment = VerticalAlignment.Top;

                for (int c = 0; c < reportRow.Cells.Count && c < report.Columns.Count; c++)
                {
                    var reportCell = reportRow.Cells[c];
                    var cell = row.Cells[c];

                    if (reportCell.HighlightTag == "changed")
                        cell.Shading.Color = HighlightChanged;

                    var para = cell.AddParagraph();

                    // Tags, then (when present) the AI-generated one-line
                    // explanation, render at the top of the first cell only
                    // - the PDF's equivalent of the HTML export's card
                    // header + highlighted change-summary line.
                    if (c == 0 && reportRow.Tags.Count > 0)
                    {
                        foreach (var tag in reportRow.Tags)
                        {
                            var run = para.AddFormattedText(tag.Text, TextFormat.Bold);
                            run.Font.Color = TagColors.GetValueOrDefault(tag.ColorTag, TagColors["neutral"]);
                            para.AddText("   ");
                        }
                        para.AddLineBreak();
                    }

                    if (c == 0 && !string.IsNullOrWhiteSpace(reportRow.Explanation))
                    {
                        var explRun = para.AddFormattedText(reportRow.Explanation, TextFormat.Bold);
                        explRun.Font.Color = BrandDark;
                        explRun.Font.Size = 7.5;
                        para.AddLineBreak();
                    }

                    para.AddText(string.IsNullOrWhiteSpace(reportCell.Text) ? "—" : reportCell.Text);
                }
            }

            var groupSpacer = section.AddParagraph();
            groupSpacer.Format.SpaceAfter = Unit.FromCentimeter(0.15);
        }
    }

    private static int CompareGroupCode(string a, string b)
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
}
