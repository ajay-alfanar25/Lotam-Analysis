using Microsoft.AspNetCore.Mvc;

namespace LotamSentinel.Api.Controllers;

/// <summary>Trivial, unauthenticated liveness check - GET /api/health
/// returns a plain 200. Used to confirm a deploy is actually up (see
/// DEPLOYMENT.md), and as the target for an optional external keep-alive
/// ping (e.g. cron-job.org) that stops Render's free tier from spinning the
/// API down during business hours. Deliberately does nothing - no DB call -
/// so it stays fast and can't itself fail because of a downstream outage;
/// if you need a "is the DB reachable" check specifically, that's a
/// different, deeper endpoint than this one.</summary>
[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", utc = DateTimeOffset.UtcNow });
}
