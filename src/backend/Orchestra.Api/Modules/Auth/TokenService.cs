using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Modules.Auth;

public sealed class TokenService(AuthSettings settings)
{
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public LoginResponse AccessToken(AppUser user, Guid sessionId)
    {
        var expiry = DateTime.UtcNow.AddMinutes(settings.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new Claim(ClaimTypes.Role, user.RoleCode),
            new Claim("stamp", user.SecurityStamp.ToString()),
            new Claim("sid", sessionId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        return new LoginResponse(Create(claims, expiry), expiry,
            new UserDto(user.UserId, user.Email, user.FirstName, user.LastName,
                user.RoleCode, user.DepartmentId, user.Status, user.MustChangePassword));
    }

    public string ResetToken(AppUser user, Guid challengeId)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new Claim("purpose", "password-reset"),
            new Claim("challenge", challengeId.ToString()),
            new Claim("stamp", user.SecurityStamp.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        return Create(claims, DateTime.UtcNow.AddMinutes(settings.ResetTokenMinutes));
    }

    public (Guid UserId, Guid ChallengeId, Guid Stamp)? ValidateResetToken(string token)
    {
        try
        {
            var principal = _handler.ValidateToken(token, ValidationParameters(), out _);
            if (principal.FindFirstValue("purpose") != "password-reset" ||
                !Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId) ||
                !Guid.TryParse(principal.FindFirstValue("challenge"), out var challengeId) ||
                !Guid.TryParse(principal.FindFirstValue("stamp"), out var stamp)) return null;
            return (userId, challengeId, stamp);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException) { return null; }
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true, ValidIssuer = settings.Issuer,
        ValidateAudience = true, ValidAudience = settings.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey!)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = JwtRegisteredClaimNames.Sub,
        RoleClaimType = ClaimTypes.Role
    };

    private string Create(IEnumerable<Claim> claims, DateTime expiresAt) => _handler.WriteToken(
        new JwtSecurityToken(settings.Issuer, settings.Audience, claims, expires: expiresAt,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey!)),
                SecurityAlgorithms.HmacSha256)));

    public static string NewOpaqueToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string HashOpaqueToken(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
