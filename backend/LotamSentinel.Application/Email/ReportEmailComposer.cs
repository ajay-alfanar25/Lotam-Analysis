using LotamSentinel.Application.Reporting;
using LotamSentinel.Application.Templating;

namespace LotamSentinel.Application.Email;

/// <summary>Everything ReportEmailComposer needs to fill in
/// Templates/Email/report-notification.html - one context class shared by
/// every export/report email (Material export, Change export, and later
/// the notification-subscription digest), so the template only has to be
/// written once. See that file for what each field becomes.</summary>
public record ReportEmailContext(
    string RecipientFirstName,
    string ReportTitle,
    List<string> IntroBullets,
    int RecordCount,
    string MaterialGroupsDisplay,
    DateTimeOffset GeneratedAt);

/// <summary>Turns a ReportEmailContext into the final HTML email body by
/// loading Templates/Email/report-notification.html and substituting its
/// __TOKEN__ placeholders - see TemplateLoader for how/where that file is
/// found, and the template itself for the full token list.</summary>
public static class ReportEmailComposer
{
    public static string Compose(ReportEmailContext ctx)
    {
        var template = TemplateLoader.Load("Email/report-notification.html");

        var bulletsHtml = string.Join(
            "\n        ",
            ctx.IntroBullets.Select(b => $"<li>{Escape(b)}</li>"));

        return template
            .Replace("__RECIPIENT_FIRST_NAME__", Escape(ctx.RecipientFirstName))
            .Replace("__REPORT_TITLE__", Escape(ctx.ReportTitle))
            .Replace("__INTRO_BULLETS__", bulletsHtml)
            .Replace("__RECORD_COUNT__", ctx.RecordCount.ToString())
            .Replace("__MATERIAL_GROUPS_DISPLAY__", Escape(ctx.MaterialGroupsDisplay))
            .Replace("__GENERATED_AT__", Escape(ctx.GeneratedAt.ToString("MMMM d, yyyy, HH:mm 'UTC'")));
    }

    /// <summary>Best-effort "Dear &lt;First Name&gt;" greeting when the
    /// recipient isn't a registered AppUser (an arbitrary "send to email"
    /// address) - takes the local part before '@', splits on the common
    /// separators, and title-cases the first piece. "ajay.kumar@x.com" ->
    /// "Ajay". Falls back to "Colleague" for anything empty/unusual rather
    /// than guessing wrong.</summary>
    public static string DeriveFirstNameFromEmail(string email)
    {
        var local = email.Split('@', 2)[0];
        var first = local.Split('.', '_', '-', '+').FirstOrDefault(p => p.Length > 0);
        if (string.IsNullOrWhiteSpace(first)) return "Colleague";
        return char.ToUpperInvariant(first[0]) + (first.Length > 1 ? first[1..].ToLowerInvariant() : "");
    }

    /// <summary>The "52, 53.1, +4 more"/"All" display used by every export
    /// email's Material Groups row - factored out here so ReleasesController,
    /// ChangesController, and SubscriptionDigestWorker all compute it the
    /// same way instead of three near-identical copies.</summary>
    public static string BuildGroupsDisplay(ExportReport report)
    {
        var groupCodes = report.Rows
            .Select(r => r.GroupCode).Where(c => !string.IsNullOrWhiteSpace(c) && c != "—")
            .Distinct().OrderBy(c => c).ToList();
        return groupCodes.Count == 0 ? "All"
            : groupCodes.Count <= 12 ? string.Join(", ", groupCodes)
            : string.Join(", ", groupCodes.Take(12)) + $", +{groupCodes.Count - 12} more";
    }

    private static string Escape(string s) => System.Net.WebUtility.HtmlEncode(s);
}
