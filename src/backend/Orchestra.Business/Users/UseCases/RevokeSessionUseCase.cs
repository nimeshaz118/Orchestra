
using System.Data.Common;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Modules.Users.UseCases;

public sealed class RevokeSessionUseCase
{
    private const string ForcedRevokeReason = "forced revoke";
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SessionLocks = [];

    private readonly OrchestraDbContext db;

    public RevokeSessionUseCase(OrchestraDbContext db)
    {
        this.db = db;
    }

    public async Task ExecuteAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken ct)
    {
        var sessionLock = SessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(ct);
        try
        {
            RefreshToken? session;
            try
            {
                session = await db.RefreshTokens.FirstOrDefaultAsync(
                    x => x.RefreshTokenId == sessionId && x.UserId == userId,
                    ct);
            }
            catch (DbException)
            {
                throw new ApiException(
                    500,
                    "DATABASE_OPERATION_FAILED",
                    "The session could not be revoked.");
            }

            if (session is null)
                throw new ApiException(404, "SESSION_NOT_FOUND", "The session was not found.");
            if (session.RevokedAt is not null)
                throw new ApiException(
                    409,
                    "SESSION_ALREADY_REVOKED",
                    "The session has already been revoked.");

            session.RevokedAt = DateTime.UtcNow;
            session.RevokedReason = ForcedRevokeReason;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                throw new ApiException(
                    500,
                    "DATABASE_OPERATION_FAILED",
                    "The session could not be revoked.");
            }
        }
        finally
        {
            sessionLock.Release();
        }
    }
}
