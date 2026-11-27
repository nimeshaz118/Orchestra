using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Data;

namespace Orchestra.Api.Modules.Auth;

/// <summary>
/// Result of checking an access token's user and session against the database.
/// When <see cref="FailureMessage"/> is null the token is accepted.
/// </summary>
public sealed record SessionValidationResult(
    string? FailureMessage,
    bool AccountUnavailable = false,
    bool MustChangePassword = false)
{
    public bool Succeeded => FailureMessage is null;
}

/// <summary>
/// Checks, on every authenticated request, that the JWT still belongs to an existing user
/// with a matching security stamp and role, and an active (not revoked, not expired) session.
/// Called from the JWT bearer OnTokenValidated event in Program.cs.
/// </summary>
public sealed class SessionValidator(OrchestraDbContext db)
{
    public async Task<SessionValidationResult> ValidateAsync(ClaimsPrincipal? principal, CancellationToken ct)
    {
        var rawId = principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var rawStamp = principal?.FindFirstValue("stamp");
        if (!Guid.TryParse(rawId, out var id) || !Guid.TryParse(rawStamp, out var stamp) ||
            !Guid.TryParse(principal?.FindFirstValue("sid"), out var sessionId))
            return new SessionValidationResult("Invalid identity.");

        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == id);
        if (user is null || user.DeletedAt != null || user.SecurityStamp != stamp ||
            !principal!.IsInRole(user.RoleCode))
            return new SessionValidationResult("Invalid identity.");

        if (!await db.RefreshTokens.AsNoTracking().AnyAsync(t => t.UserId == id &&
            t.FamilyId == sessionId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow, ct))
            return new SessionValidationResult("Session is no longer active.");

        if (user.Status != "ACTIVE" || user.EmailVerifiedAt == null)
            return new SessionValidationResult("Account is unavailable.", AccountUnavailable: true);

        return new SessionValidationResult(null, MustChangePassword: user.MustChangePassword);
    }
}
