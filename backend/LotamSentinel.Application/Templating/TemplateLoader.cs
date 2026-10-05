using System.Collections.Concurrent;

namespace LotamSentinel.Application.Templating;

/// <summary>Reads a template file's content from the common "Templates"
/// folder that ships next to the running app - physically owned by
/// LotamSentinel.Api (see its Templates/ directory and .csproj, which
/// copies the whole tree to the build output). AppContext.BaseDirectory is
/// the entry app's own bin folder regardless of which project this class
/// lives in, so this works the same whether it's called from a controller
/// or from deep inside the Application layer.
///
/// Every consumer (ReportEmailComposer, HtmlReportRenderer) does its own
/// token substitution on the string this returns - this class only loads
/// and caches the raw file, it knows nothing about tokens.</summary>
public static class TemplateLoader
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <param name="relativePath">Forward-slash path under Templates/, e.g.
    /// "Email/report-notification.html" or "Reports/report.css".</param>
    public static string Load(string relativePath)
    {
        return Cache.GetOrAdd(relativePath, static path =>
        {
            var normalized = path.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(AppContext.BaseDirectory, "Templates", normalized);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException(
                    $"Template not found at '{fullPath}'. Expected it under " +
                    $"LotamSentinel.Api/Templates/{path} in source, copied to the build output by " +
                    "LotamSentinel.Api.csproj's <None Include=\"Templates\\**\\*\" ... /> item.",
                    fullPath);
            return File.ReadAllText(fullPath);
        });
    }
}
