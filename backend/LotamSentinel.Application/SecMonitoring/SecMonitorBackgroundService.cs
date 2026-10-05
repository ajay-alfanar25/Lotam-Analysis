using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LotamSentinel.Application.SecMonitoring;

/// <summary>
/// Runs SecMonitorService once daily. This is Track A from the project's
/// planning discussion (a plain scheduled job, not an AI agent) -
/// deliberately simple, built entirely on Microsoft.Extensions.Hosting
/// (already part of ASP.NET Core, no new infrastructure dependency).
/// The manual "check now" button on the Release Audit page calls the exact
/// same ISecMonitorService, so scheduled and manual checks can never
/// disagree in behavior.
/// </summary>
public class SecMonitorBackgroundService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _services;
    private readonly ILogger<SecMonitorBackgroundService> _logger;

    public SecMonitorBackgroundService(IServiceProvider services, ILogger<SecMonitorBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = _services.CreateScope())
            {
                var monitor = scope.ServiceProvider.GetRequiredService<ISecMonitorService>();
                try
                {
                    var result = await monitor.CheckForNewReleasesAsync(stoppingToken);
                    if (result.Configured && result.NewDocumentsFound > 0)
                    {
                        _logger.LogInformation(
                            "SEC monitor: found {Count} new document link(s) - visible on the Release Audit page.",
                            result.NewDocumentsFound);
                    }
                }
                catch (Exception ex)
                {
                    // Never let one failed scheduled check take down the
                    // background service permanently - log and retry on
                    // the next interval.
                    _logger.LogWarning(ex, "SEC monitor scheduled check threw unexpectedly");
                }
            }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (TaskCanceledException) { }
        }
    }
}
