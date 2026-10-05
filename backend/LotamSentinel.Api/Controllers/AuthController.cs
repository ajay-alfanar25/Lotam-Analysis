using System.Security.Claims;
using System.Security.Cryptography;
using LotamSentinel.Api.Dtos;
using LotamSentinel.Application.Auth;
using LotamSentinel.Application.Email;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly LotamSentinelDbContext _db;
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly IJwtTokenService _jwt;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        LotamSentinelDbContext db, IPasswordHasher<AppUser> passwordHasher, IJwtTokenService jwt,
        IEmailSender emailSender, ILogger<AuthController> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _emailSender = emailSender;
        _logger = logger;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponseDto>> Login(LoginRequest request)
    {
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.Trim().ToLower());
        if (user is null || !user.IsActive)
            return Unauthorized(new GenericMessageDto("Invalid email or password."));

        var verify = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verify == PasswordVerificationResult.Failed)
            return Unauthorized(new GenericMessageDto("Invalid email or password."));

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var token = _jwt.GenerateToken(user.Id, user.Email, user.FullName, user.IsAdmin);
        return Ok(new LoginResponseDto(token, ToUserDto(user)));
    }

    /// <summary>Always returns the same generic message whether or not the
    /// email exists - avoids letting the reset form be used to discover
    /// which addresses have accounts. If it does exist, a 6-digit OTP
    /// (hashed at rest, 10-minute expiry, single-use) is emailed via
    /// IEmailSender - see README "Email / SMTP setup" if this throws
    /// because SMTP isn't configured yet.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ActionResult<GenericMessageDto>> ForgotPassword(ForgotPasswordRequest request)
    {
        const string genericMessage = "If that email has an account, a reset code has been sent to it.";

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.Trim().ToLower());
        if (user is null || !user.IsActive)
            return Ok(new GenericMessageDto(genericMessage));

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var otp = new PasswordResetOtp
        {
            UserId = user.Id,
            CodeHash = _passwordHasher.HashPassword(user, code),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            Used = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.PasswordResetOtps.Add(otp);
        await _db.SaveChangesAsync();

        try
        {
            await _emailSender.SendAsync(
                user.Email,
                "LOTAM Sentinel — Password reset code",
                $"""
                <p>Hello {System.Net.WebUtility.HtmlEncode(user.FullName)},</p>
                <p>Your LOTAM Sentinel password reset code is:</p>
                <p style="font-size:28px; font-weight:700; letter-spacing:4px;">{code}</p>
                <p>This code expires in 10 minutes and can only be used once. If you didn't request this, you can ignore this email.</p>
                """);
        }
        catch (InvalidOperationException ex)
        {
            // SMTP not configured yet - don't leak that detail to an
            // unauthenticated caller, but do log it so whoever's setting up
            // SMTP can see why resets aren't arriving.
            _logger.LogWarning(ex, "Password reset email could not be sent - SMTP is not configured.");
        }

        return Ok(new GenericMessageDto(genericMessage));
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<ActionResult<GenericMessageDto>> ResetPassword(ResetPasswordRequest request)
    {
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.Trim().ToLower());
        if (user is null)
            return BadRequest(new GenericMessageDto("Invalid or expired code."));

        var otp = await _db.PasswordResetOtps
            .Where(o => o.UserId == user.Id && !o.Used && o.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otp is null || _passwordHasher.VerifyHashedPassword(user, otp.CodeHash, request.Otp) == PasswordVerificationResult.Failed)
            return BadRequest(new GenericMessageDto("Invalid or expired code."));

        otp.Used = true;
        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        await _db.SaveChangesAsync();

        return Ok(new GenericMessageDto("Password updated - you can now log in with your new password."));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<GenericMessageDto>> ChangePassword(ChangePasswordRequest request)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            return BadRequest(new GenericMessageDto("Current password is incorrect."));

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        await _db.SaveChangesAsync();
        return Ok(new GenericMessageDto("Password changed."));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me()
    {
        var user = await CurrentUserAsync();
        return user is null ? Unauthorized() : Ok(ToUserDto(user));
    }

    private async Task<AppUser?> CurrentUserAsync()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (sub is null || !long.TryParse(sub, out var userId)) return null;
        return await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId);
    }

    private static UserDto ToUserDto(AppUser u) => new(u.Id, u.Email, u.FullName, u.Mobile, u.Address, u.Department, u.IsAdmin);
}
