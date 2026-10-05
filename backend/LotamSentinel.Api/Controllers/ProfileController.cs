using System.Security.Claims;
using LotamSentinel.Api.Dtos;
using LotamSentinel.Application.Notifications;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

/// <summary>The logged-in user's own info + notification subscriptions
/// (Profile page, tabs 1 and 2). Everything here is scoped to the calling
/// user via the JWT's subject claim - there is no id parameter anywhere,
/// deliberately, so nobody can read or edit another user's profile just by
/// changing a number in the URL.</summary>
[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    private readonly SubscriptionDigestQueue _digestQueue;

    public ProfileController(LotamSentinelDbContext db, SubscriptionDigestQueue digestQueue)
    {
        _db = db;
        _digestQueue = digestQueue;
    }

    [HttpGet]
    public async Task<ActionResult<UserDto>> Get()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();
        return Ok(new UserDto(user.Id, user.Email, user.FullName, user.Mobile, user.Address, user.Department, user.IsAdmin));
    }

    [HttpPut]
    public async Task<ActionResult<UserDto>> Update(UpdateProfileRequest request)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        user.FullName = request.FullName;
        user.Mobile = request.Mobile;
        user.Address = request.Address;
        user.Department = request.Department;
        await _db.SaveChangesAsync();

        return Ok(new UserDto(user.Id, user.Email, user.FullName, user.Mobile, user.Address, user.Department, user.IsAdmin));
    }

    [HttpGet("notification-subscriptions")]
    public async Task<ActionResult<List<NotificationSubscriptionDto>>> GetSubscriptions()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        var rows = await _db.UserNotificationSubscriptions
            .Include(s => s.Recipients)
            .Where(s => s.UserId == user.Id)
            .OrderBy(s => s.SubscriptionType).ThenBy(s => s.TargetLabel)
            .ToListAsync();

        return Ok(rows.Select(ToDto).ToList());
    }

    [HttpPost("notification-subscriptions")]
    public async Task<ActionResult<NotificationSubscriptionDto>> AddSubscription(AddNotificationSubscriptionRequest request)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        if (!Enum.TryParse<NotificationSubscriptionType>(request.SubscriptionType, true, out var type))
            return BadRequest(new GenericMessageDto("SubscriptionType must be one of: MaterialCategory, MaterialSection, Technology, Supplier."));

        var existing = await _db.UserNotificationSubscriptions
            .Include(s => s.Recipients)
            .FirstOrDefaultAsync(s => s.UserId == user.Id && s.SubscriptionType == type && s.TargetCode == request.TargetCode);
        if (existing is not null)
            return Ok(ToDto(existing));

        var sub = new UserNotificationSubscription
        {
            UserId = user.Id,
            SubscriptionType = type,
            TargetCode = request.TargetCode,
            TargetLabel = request.TargetLabel,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.UserNotificationSubscriptions.Add(sub);
        await _db.SaveChangesAsync();

        return Ok(ToDto(sub));
    }

    [HttpDelete("notification-subscriptions/{id:long}")]
    public async Task<IActionResult> RemoveSubscription(long id)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        var sub = await _db.UserNotificationSubscriptions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id);
        if (sub is null) return NotFound();

        _db.UserNotificationSubscriptions.Remove(sub);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Extra email addresses attached to one subscription - the
    /// "who else should hear about this group" list under each row of
    /// Notification Settings' "Subscribed to" panel. All three actions
    /// re-check that the subscription belongs to the calling user first, so
    /// nobody can add/remove recipients on someone else's subscription by
    /// guessing an id.</summary>
    [HttpPost("notification-subscriptions/{subscriptionId:long}/recipients")]
    public async Task<ActionResult<SubscriptionRecipientDto>> AddRecipient(long subscriptionId, AddSubscriptionRecipientRequest request)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        var sub = await _db.UserNotificationSubscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == user.Id);
        if (sub is null) return NotFound();

        var email = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new GenericMessageDto("Email is required."));

        var existing = await _db.SubscriptionRecipients.FirstOrDefaultAsync(
            r => r.SubscriptionId == subscriptionId && r.Email.ToLower() == email.ToLower());
        if (existing is not null)
            return Ok(new SubscriptionRecipientDto(existing.Id, existing.SubscriptionId, existing.Email, existing.Name, existing.CreatedAt));

        var recipient = new SubscriptionRecipient
        {
            SubscriptionId = subscriptionId,
            Email = email,
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.SubscriptionRecipients.Add(recipient);
        await _db.SaveChangesAsync();

        return Ok(new SubscriptionRecipientDto(recipient.Id, recipient.SubscriptionId, recipient.Email, recipient.Name, recipient.CreatedAt));
    }

    [HttpDelete("notification-subscriptions/{subscriptionId:long}/recipients/{recipientId:long}")]
    public async Task<IActionResult> RemoveRecipient(long subscriptionId, long recipientId)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        var sub = await _db.UserNotificationSubscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == user.Id);
        if (sub is null) return NotFound();

        var recipient = await _db.SubscriptionRecipients
            .FirstOrDefaultAsync(r => r.Id == recipientId && r.SubscriptionId == subscriptionId);
        if (recipient is null) return NotFound();

        _db.SubscriptionRecipients.Remove(recipient);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>"Send digest now" - the Notification Settings tab's manual
    /// trigger. Queues one job per subscription the calling user owns (see
    /// SubscriptionDigestQueue/SubscriptionDigestWorker for how those are
    /// processed - one at a time, in the background) and returns
    /// immediately with how many were queued, rather than waiting for any
    /// of them to actually send. Building a report and emailing every
    /// recipient for even one subscription can take several seconds; doing
    /// that for all of them inline on this request would risk a browser/
    /// gateway timeout on exactly the accounts with the most subscriptions.</summary>
    [HttpPost("notification-subscriptions/send-digest")]
    public async Task<ActionResult<SendDigestResultDto>> SendDigest()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        var subscriptionIds = await _db.UserNotificationSubscriptions
            .Where(s => s.UserId == user.Id)
            .Select(s => s.Id)
            .ToListAsync();

        if (subscriptionIds.Count == 0)
            return BadRequest(new GenericMessageDto("You have no subscriptions to send a digest for - add one above first."));

        var releaseCount = await _db.LotamReleases.CountAsync();
        if (releaseCount < 2)
            return BadRequest(new GenericMessageDto(
                "At least two LOTAM releases are needed to detect changes for a digest - only one release has been imported so far."));

        foreach (var id in subscriptionIds)
            _digestQueue.Enqueue(new SubscriptionDigestJob(id));

        return Ok(new SendDigestResultDto(subscriptionIds.Count));
    }

    private static NotificationSubscriptionDto ToDto(UserNotificationSubscription s) => new(
        s.Id, s.SubscriptionType.ToString(), s.TargetCode, s.TargetLabel, s.CreatedAt,
        s.Recipients.Select(r => new SubscriptionRecipientDto(r.Id, r.SubscriptionId, r.Email, r.Name, r.CreatedAt)).ToList());

    /// <summary>What can be subscribed to, for the settings tab's pickers.
    /// Suppliers are searched via the existing GET /api/suppliers instead
    /// of duplicated here - the list is too large to return in full.
    ///
    /// Material sections (specific material groups/subgroups within a
    /// category, e.g. "52.2") are scoped to the single most recent LOTAM
    /// release, not any comparison pair - this page doesn't use the global
    /// Previous/Current selector (see app.component.ts's HIDE_TOPBAR_ROUTES)
    /// and always means "pick from what's current right now".</summary>
    [HttpGet("subscribable-options")]
    public async Task<ActionResult<SubscribableOptionsDto>> GetSubscribableOptions()
    {
        var categories = await _db.MaterialCategories
            .Select(c => c.Name).Distinct().OrderBy(n => n).ToListAsync();
        var technologies = await _db.Technologies
            .OrderBy(t => t.Name)
            .Select(t => new TechnologyDto(t.Id, t.Code, t.Name, t.Description))
            .ToListAsync();

        var latestReleaseId = await _db.LotamReleases
            .OrderByDescending(r => r.ReleaseDate)
            .Select(r => (long?)r.Id)
            .FirstOrDefaultAsync();

        var materialSections = new List<SubscribableMaterialDto>();
        if (latestReleaseId is not null)
        {
            var sections = await _db.MaterialSections
                .Include(m => m.MaterialCategory)
                .Where(m => m.ReleaseId == latestReleaseId)
                .ToListAsync();
            // In-memory, numeric-aware sort ("1.1" < "1.10" < "1.2") - the
            // same convention used everywhere else material codes are
            // ordered (see ReleasesController.ParseSortKey); a plain SQL
            // ORDER BY on the text code would put "1.10" before "1.2".
            materialSections = sections
                .OrderBy(m => ParseSortKey(m.Code))
                .Select(m => new SubscribableMaterialDto(m.Code, $"{m.MaterialCategory.Name} — {m.RawDescription}"))
                .ToList();
        }

        return Ok(new SubscribableOptionsDto(categories, technologies, materialSections));
    }

    private static (int, int) ParseSortKey(string code)
    {
        var parts = code.Split('.', 2);
        int.TryParse(parts[0], out var a);
        int.TryParse(parts.Length > 1 ? parts[1] : "0", out var b);
        return (a, b);
    }

    private async Task<AppUser?> CurrentUserAsync()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (sub is null || !long.TryParse(sub, out var userId)) return null;
        return await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId);
    }
}
