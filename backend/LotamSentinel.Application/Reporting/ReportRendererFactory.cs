namespace LotamSentinel.Application.Reporting;

/// <summary>Picks the right IReportRenderer for a request's ?format=
/// parameter. Both PdfReportRenderer and HtmlReportRenderer are registered
/// as IReportRenderer in Program.cs; this factory is the only place that
/// knows there's more than one.</summary>
public interface IReportRendererFactory
{
    IReportRenderer Get(string? format);
}

public class ReportRendererFactory : IReportRendererFactory
{
    private readonly IEnumerable<IReportRenderer> _renderers;

    public ReportRendererFactory(IEnumerable<IReportRenderer> renderers) => _renderers = renderers;

    public IReportRenderer Get(string? format)
    {
        var key = string.IsNullOrWhiteSpace(format) ? "pdf" : format.Trim().ToLowerInvariant();
        return _renderers.FirstOrDefault(r => r.FormatKey == key)
            ?? throw new ArgumentException(
                $"Unknown export format '{format}'. Supported: {string.Join(", ", _renderers.Select(r => r.FormatKey))}.");
    }
}
