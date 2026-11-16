namespace Orchestra.Api.Modules.Users.Interfaces;
public interface ISessionService
{
    Task RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken ct);

    Task<int> RevokeOtherSessionsAsync(
        Guid userId,
        string? currentToken,
        CancellationToken ct);
}
