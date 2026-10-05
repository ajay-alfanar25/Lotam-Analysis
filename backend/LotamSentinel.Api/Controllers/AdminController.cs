using System.Security.Claims;
using System.Security.Cryptography;
using LotamSentinel.Api.Dtos;
using LotamSentinel.Application.Email;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

/// <summary>User management for admins only - the Profile page's "Admin"
/// tab (shown only when UserDto.IsAdmin is true). There is still no public
/// sign-up endpoint; this is the intended way to provision every user after
/// the bootstrap admin (see Program.cs and README "Logging in").
///
/// [Authorize] gets everyone past authentication; RequireAdmin() below is
/// the actual admin gate, checked per-action against the "isAdmin" claim
/// baked into the JWT at login (JwtTokenService) - never against a value
/// read from the request body, so a non-admin can't just pass isAdmin:true
/// on an unrelated call.</summary>
[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        LotamSentinelDbContext db, IPasswordHasher<AppUser> passwordHasher,
        IEmailSender emailSender, ILogger<AdminController> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _emailSender = emailSender;
        _logger = logger;
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<AdminUserDto>>> ListUsers()
    {
        if (RequireAdmin() is { } forbidden) return forbidden;

        var users = await _db.AppUsers.OrderBy(u => u.FullName).ToListAsync();
        return Ok(users.Select(ToAdminDto).ToList());
    }

    /// <summary>Creates the new login directly (no separate "accept
    /// invite" step - keeps this to one round trip) with a random temporary
    /// password, then emails it to them along with a note to change it
    /// after their first login. If SMTP isn't configured yet, the user is
    /// still created - EmailSent just comes back false so the admin knows
    /// to pass the password along another way.</summary>
    [HttpPost("users/invite")]
    public async Task<ActionResult<InviteUserResultDto>> InviteUser(InviteUserRequest request)
    {
        if (RequireAdmin() is { } forbidden) return forbidden;

        var email = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest(new GenericMessageDto("Email and full name are required."));

        var exists = await _db.AppUsers.AnyAsync(u => u.Email.ToLower() == email.ToLower());
        if (exists)
            return BadRequest(new GenericMessageDto("A user with that email already exists."));

        var tempPassword = GenerateTempPassword();
        var user = new AppUser
        {
            Email = email,
            FullName = request.FullName.Trim(),
            Department = request.Department,
            IsActive = true,
            IsAdmin = request.IsAdmin,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, tempPassword);
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();

        var emailSent = true;
        try
        {
            await _emailSender.SendAsync(
                user.Email,
                "You've been invited to LOTAM Sentinel",
                $"""
                <p>Hello {System.Net.WebUtility.HtmlEncode(user.FullName)},</p>
                <p>An administrator has created a LOTAM Sentinel account for you.</p>
                <p><strong>Email:</strong> {System.Net.WebUtility.HtmlEncode(user.Email)}<br/>
                <strong>Temporary password:</strong> {System.Net.WebUtility.HtmlEncode(tempPassword)}</p>
                <p>Log in and change your password from your Profile page as soon as you can. If you ever forget it, use "Forgot password" on the login screen to reset it by email.</p>
                """);
        }
        catch (InvalidOperationException ex)
        {
            emailSent = false;
            _logger.LogWarning(ex,
                "Invite email to {Email} could not be sent - SMTP is not configured. Temporary password: {TempPassword}",
                user.Email, tempPassword);
        }

        return Ok(new InviteUserResultDto(ToAdminDto(user), emailSent));
    }

    /// <summary>Activate/deactivate a login - the soft alternative to
    /// deleting a user outright (keeps their history/subscriptions intact,
    /// just blocks future logins via AuthController.Login's IsActive check).
    /// An admin can't deactivate themselves, to avoid locking everyone out
    /// if they're the only admin.</summary>
    [HttpPut("users/{id:long}/active")]
    public async Task<ActionResult<AdminUserDto>> SetActive(long id, SetUserActiveRequest request)
    {
        if (RequireAdmin() is { } forbidden) return forbidden;

        var currentId = CurrentUserId();
        if (currentId == id)
            return BadRequest(new GenericMessageDto("You can't deactivate your own account."));

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        user.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Ok(ToAdminDto(user));
    }

    private static string GenerateTempPassword()
    {
        // Avoids visually-ambiguous characters (0/O, 1/I/l) since this is
        // read and retyped by a human on first login.
        const string chars = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        var bytes = new char[14];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        return new string(bytes);
    }

    private static AdminUserDto ToAdminDto(AppUser u) =>
        new(u.Id, u.Email, u.FullName, u.Department, u.IsActive, u.IsAdmin, u.CreatedAt, u.LastLoginAt);

    private long? CurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return sub is not null && long.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>Returns a 403 ActionResult when the caller isn't an admin,
    /// or null to let the action proceed - checked against the JWT claim
    /// set at login, not re-read from the database, so this stays a cheap
    /// per-request check.</summary>
    private ActionResult? RequireAdmin()
    {
        var isAdmin = User.FindFirstValue("isAdmin");
        return isAdmin == "true" ? null : Forbid();
    }
}
