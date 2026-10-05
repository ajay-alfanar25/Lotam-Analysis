using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LotamSentinel.Application.Auth;

public class JwtOptions
{
    /// <summary>Signing key - must be set in appsettings.json (or an
    /// environment variable override) and be at least 32 characters. There
    /// is no baked-in default on purpose: a shared default secret would
    /// mean anyone with a copy of this codebase could mint valid tokens for
    /// every deployment of it.</summary>
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "LotamSentinel";
    public string Audience { get; set; } = "LotamSentinelClient";
    public int ExpiryMinutes { get; set; } = 480; // 8 hours
}

public interface IJwtTokenService
{
    string GenerateToken(long userId, string email, string fullName, bool isAdmin);
}

/// <summary>Issues the JWT handed back on login. Verification of that same
/// token on every subsequent request is wired up separately in Program.cs
/// via AddJwtBearer, using the identical Jwt:Secret/Issuer/Audience - the
/// two must always agree, since they're really the same contract.</summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public string GenerateToken(long userId, string email, string fullName, bool isAdmin)
    {
        if (string.IsNullOrWhiteSpace(_options.Secret) || _options.Secret.Length < 32)
            throw new InvalidOperationException(
                "Jwt:Secret is missing or too short in appsettings.json - it must be at least 32 characters.");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("fullName", fullName),
            // Trusted only because it's inside a signed token minted right
            // here from the AppUser row - AdminController re-reads it off
            // User.Claims rather than ever taking an isAdmin value from a
            // request body.
            new Claim("isAdmin", isAdmin ? "true" : "false"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
