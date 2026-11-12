using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Modules.Auth;

namespace Orchestra.Api.Modules.Users.UseCases;
public sealed class RevokeOtherSessionsUseCase
{
    private const string ForcedRevokeReason = "forced revoke";
    private readonly OrchestraDbContext db;
    public RevokeOtherSessionsUseCase(OrchestraDbContext db)
    {
        this.db = db;
    }

    public async Task<int> ExecuteAsync(
        Guid userId,
        string? currentToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentToken))
            throw new ApiException(
                401,
                "INVALID_SESSION",
                "The session has expired.");

        var currentHash =
            TokenService.HashOpaqueToken(currentToken);
       
        var now = DateTime.UtcNow;

        var sessions = await db.RefreshTokens
            .Where(x =>
                x.UserId == userId &&
                x.RevokedAt == null &&
                x.ExpiresAt > now &&
                x.TokenHash != currentHash)
            .ToListAsync(ct);

        foreach (var session in sessions)
        {
            session.RevokedAt = now;
            session.RevokedReason = ForcedRevokeReason;
        }

        await db.SaveChangesAsync(ct);
        var revokedCount = sessions.Count;
                
        if (revokedCount == 0)
            throw new ApiException(
                409,
                "ALL_OTHER_SESSIONS_REVOKED",
                "All other sessions have already been revoked.");

        return revokedCount;
    }
}
