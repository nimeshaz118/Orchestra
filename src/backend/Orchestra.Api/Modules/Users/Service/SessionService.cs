using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Orchestra.Api.Models.Entities;
using Orchestra.Api.Modules.Users.UseCases;
using Orchestra.Api.Modules.Users.Interfaces;

namespace Orchestra.Api.Modules.Users.Service;
public sealed class SessionService : ISessionService

{
    private readonly RevokeSessionUseCase revokeSession;
    private readonly RevokeOtherSessionsUseCase revokeOtherSessions;

    public SessionService(
        RevokeSessionUseCase revokeSession,
        RevokeOtherSessionsUseCase revokeOtherSessions)
    {
        this.revokeSession = revokeSession;
        this.revokeOtherSessions = revokeOtherSessions;
    }

    public async Task RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken ct)
    {
        await revokeSession.ExecuteAsync(userId, sessionId, ct);
    }

    public async Task<int> RevokeOtherSessionsAsync(
        Guid userId,
        string? currentToken,
        CancellationToken ct)
    {
        return await revokeOtherSessions.ExecuteAsync(
            userId,
            currentToken,
            ct);
    }
}
