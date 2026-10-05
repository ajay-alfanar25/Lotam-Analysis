using LotamSentinel.Application.Comparison;
using LotamSentinel.Application.Email;
using LotamSentinel.Application.Reporting;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LotamSentinel.Application.Notifications;

/// <summary>Drains SubscriptionDigestQueue one job at a time for the
/// lifetime of the app (registered via AddHostedService in Program.cs).
/// Each job: builds a Change Report scoped to that one subscription's
/// target (technology / material group / specific material / supplier) comparing the two
/// most recent LOTAM releases, renders it to HTML, and emails it - one
/// send per recipient, in sequence - to the subscription's owner plus
/// every SubscriptionRecipient attached to it.
///
/// A fresh DI scope (and therefore a fresh DbContext) is created per job,
/// not shared across the worker's lifetime - DbContext is not safe to
/// reuse across requests/jobs that can run arbitrarily far apart in time,
/// and the scope-per-job pattern here is the standard way a BackgroundService
/// reaches scoped services (see Microsoft's "Queued background tasks" docs).</summary>
public class SubscriptionDigestWorker : BackgroundService
{
    private readonly SubscriptionDigestQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionDigestWorker> _logger;

    public SubscriptionDigestWorker(
        SubscriptionDigestQueue queue, IServiceScopeFactory scopeFactory, ILogger<SubscriptionDigestWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(job, stoppingToken);
            }
            catch (Exception ex)
            {
                // One bad job (a deleted subscription, a transient DB blip)
                // must not kill the worker loop - every subsequent queued
                // job still needs to run.
                _logger.LogError(ex, "Subscription digest job failed for subscription {SubscriptionId}.", job.SubscriptionId);
            }
        }
    }

    private async Task ProcessAsync(SubscriptionDigestJob job, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<LotamSentinelDbContext>();
        var reportBuilder = services.GetRequiredService<ChangeReportBuilder>();
        var rendererFactory = services.GetRequiredService<IReportRendererFactory>();
        var emailSender = services.GetRequiredService<IEmailSender>();

        var sub = await db.UserNotificationSubscriptions
            .Include(s => s.User)
            .Include(s => s.Recipients)
            .FirstOrDefaultAsync(s => s.Id == job.SubscriptionId, ct);
        if (sub is null)
        {
            _logger.LogWarning("Digest job for subscription {Id} skipped - subscription no longer exists.", job.SubscriptionId);
            return;
        }

        var latestTwo = await db.LotamReleases
            .OrderByDescending(r => r.ReleaseDate)
            .Take(2)
            .Select(r => r.Id)
            .ToListAsync(ct);
        if (latestTwo.Count < 2)
        {
            _logger.LogWarning(
                "Digest job for subscription {Id} ({Label}) skipped - at least two LOTAM releases are needed to compare.",
                sub.Id, sub.TargetLabel);
            return;
        }
        var currentReleaseId = latestTwo[0];
        var previousReleaseId = latestTwo[1];

        // TargetCode's meaning depends on SubscriptionType - see
        // ChangeReportBuilder.BuildAsync's doc comment on supplierId/
        // materialCategory for why Supplier needs the numeric-id path
        // rather than a name-text search.
        string? technology = null, materialCategory = null, materialSectionCode = null;
        long? supplierId = null;
        switch (sub.SubscriptionType)
        {
            case NotificationSubscriptionType.Technology:
                technology = sub.TargetCode;
                break;
            case NotificationSubscriptionType.MaterialCategory:
                materialCategory = sub.TargetCode;
                break;
            case NotificationSubscriptionType.MaterialSection:
                materialSectionCode = sub.TargetCode;
                break;
            case NotificationSubscriptionType.Supplier:
                if (long.TryParse(sub.TargetCode, out var parsedId))
                    supplierId = parsedId;
                break;
        }

        var report = await reportBuilder.BuildAsync(
            previousReleaseId, currentReleaseId,
            technology: technology, supplier: null, severity: null, changeType: null, search: null,
            ct: ct, supplierId: supplierId, materialCategory: materialCategory, materialSectionCode: materialSectionCode);

        if (report.Rows.Count == 0)
        {
            // "Get emailed when something changes for..." - nothing changed
            // for this subscription since the previous release, so there is
            // deliberately nothing to send. Skipping (rather than emailing
            // an empty report) keeps this from training recipients to
            // ignore the digest.
            _logger.LogInformation(
                "Digest for subscription {Id} ({Label}) skipped - no changes between the two latest releases.",
                sub.Id, sub.TargetLabel);
            return;
        }

        var renderer = rendererFactory.Get("html");
        var bytes = renderer.Render(report);
        var filename = $"LOTAM-Sentinel-Digest-{Slugify(sub.TargetLabel)}-{DateTime.UtcNow:yyyy-MM-dd}.{renderer.FileExtension}";
        var groupsDisplay = ReportEmailComposer.BuildGroupsDisplay(report);
        var kindLabel = sub.SubscriptionType switch
        {
            NotificationSubscriptionType.Technology => "technology area",
            NotificationSubscriptionType.Supplier => "supplier",
            NotificationSubscriptionType.MaterialSection => "material",
            _ => "material group",
        };

        // Owner first, then every extra recipient attached to this one
        // subscription, de-duplicated case-insensitively (an extra
        // recipient might accidentally be re-added as the owner's own
        // address).
        var recipients = new List<(string Email, string? Name)> { (sub.User.Email, sub.User.FullName) };
        foreach (var r in sub.Recipients)
        {
            if (!recipients.Any(x => x.Email.Equals(r.Email, StringComparison.OrdinalIgnoreCase)))
                recipients.Add((r.Email, r.Name));
        }

        foreach (var (email, name) in recipients)
        {
            try
            {
                var firstName = !string.IsNullOrWhiteSpace(name)
                    ? (name!.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? ReportEmailComposer.DeriveFirstNameFromEmail(email))
                    : ReportEmailComposer.DeriveFirstNameFromEmail(email);

                var body = ReportEmailComposer.Compose(new ReportEmailContext(
                    RecipientFirstName: firstName,
                    ReportTitle: $"LOTAM Sentinel — Subscription Digest ({sub.TargetLabel})",
                    IntroBullets: new List<string>
                    {
                        $"Changes detected for your subscribed {kindLabel} — {sub.TargetLabel} — since the previous LOTAM release.",
                        "New supplier qualifications and status changes.",
                        "Updates to existing supplier approvals, models, and ratings.",
                    },
                    RecordCount: report.Rows.Count,
                    MaterialGroupsDisplay: groupsDisplay,
                    GeneratedAt: DateTimeOffset.UtcNow));

                await emailSender.SendAsync(
                    email,
                    $"LOTAM Sentinel — Subscription Digest: {sub.TargetLabel}",
                    body,
                    new List<EmailAttachment> { new(filename, bytes, renderer.ContentType) },
                    ct);

                _logger.LogInformation(
                    "Digest sent to {Email} for subscription {Id} ({Label}, {RecordCount} record(s)).",
                    email, sub.Id, sub.TargetLabel, report.Rows.Count);
            }
            catch (Exception ex)
            {
                // One recipient's mailbox/address being bad must not stop
                // the rest of this subscription's recipients (or the next
                // queued subscription) from being sent.
                _logger.LogError(ex, "Digest email to {Email} failed for subscription {Id} ({Label}).", email, sub.Id, sub.TargetLabel);
            }
        }
    }

    private static string Slugify(string label)
    {
        var cleaned = new string(label.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').ToArray()).Trim();
        return cleaned.Length == 0 ? "Subscription" : cleaned.Replace(' ', '-');
    }
}
