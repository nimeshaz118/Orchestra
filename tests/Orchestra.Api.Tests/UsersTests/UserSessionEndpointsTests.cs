using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Data;
using Orchestra.Api.Modules.Auth;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class UserSessionEndpointsTests
{
    [Fact]
    public async Task Get_me_returns_the_authenticated_user()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("me@example.test", "USER");
        var client = await host.LoginClientAsync("me@example.test");

        var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(user);
        Assert.Equal("me@example.test", user.Email);
        Assert.Equal("Test", user.FirstName);
        Assert.Equal("User", user.LastName);
        Assert.Equal("USER", user.Role);
    }

    [Fact]
    public async Task Get_sessions_returns_the_current_session()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("sessions@example.test", "USER");
        var client = await host.LoginClientAsync("sessions@example.test");

        var response = await client.GetAsync("/api/v1/me/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionDto>>();
        var session = Assert.Single(sessions!);
        Assert.True(session.IsCurrent);
        Assert.True(session.ExpiresAt > session.CreatedAt);
    }

    [Fact]
    public async Task Revoke_others_keeps_current_session_and_revokes_the_rest()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("revoke-others@example.test", "USER");
        var currentClient = await host.LoginClientAsync("revoke-others@example.test");
        _ = await host.LoginClientAsync("revoke-others@example.test");
        _ = await host.LoginClientAsync("revoke-others@example.test");

        var response = await currentClient.DeleteAsync("/api/v1/me/sessions/others");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RevokeResponse>();
        Assert.Equal("Other sessions revoked successfully.", result?.Message);
        Assert.Equal(2, result?.RevokedCount);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var sessions = await db.RefreshTokens.ToListAsync();
        Assert.Equal(3, sessions.Count);
        Assert.Single(sessions, session => session.RevokedAt is null);
        Assert.Equal(2, sessions.Count(session =>
            session.RevokedAt is not null && session.RevokedReason == "forced revoke"));
    }

    [Fact]
    public async Task Revoke_selected_session_revokes_only_the_requested_session()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("revoke-selected@example.test", "USER");
        var currentClient = await host.LoginClientAsync("revoke-selected@example.test");
        var otherClient = await host.LoginClientAsync("revoke-selected@example.test");

        var sessionsResponse = await currentClient.GetAsync("/api/v1/me/sessions");
        var sessions = await sessionsResponse.Content.ReadFromJsonAsync<List<SessionDto>>();
        var selected = Assert.Single(sessions!, session => !session.IsCurrent);

        var revokeResponse = await currentClient.DeleteAsync(
            $"/api/v1/me/sessions/{selected.Id}");

        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);
        var result = await revokeResponse.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Session revoked successfully.", result?.Message);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var revoked = await db.RefreshTokens.SingleAsync(
            session => session.RefreshTokenId == selected.Id);
        Assert.NotNull(revoked.RevokedAt);
        Assert.Equal("forced revoke", revoked.RevokedReason);

        var currentSessionsResponse = await currentClient.GetAsync("/api/v1/me/sessions");
        var currentSessions = await currentSessionsResponse.Content
            .ReadFromJsonAsync<List<SessionDto>>();
        var currentSession = Assert.Single(currentSessions!);
        Assert.True(currentSession.IsCurrent);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await otherClient.GetAsync("/api/v1/me/sessions")).StatusCode);
    }

    [Fact]
    public async Task Revoke_selected_session_cannot_revoke_another_users_session()
    {
        // Arrange
        await using var host = new ApiHost();

        await host.SeedUserAsync("user-a@example.test", "USER");
        await host.SeedUserAsync("user-b@example.test", "USER");

        var userAClient = await host.LoginClientAsync("user-a@example.test");
        var userBClient = await host.LoginClientAsync("user-b@example.test");

        // Get User B's session
        var sessionsResponse = await userBClient.GetAsync(
            "/api/v1/me/sessions");

        var sessions = await sessionsResponse.Content
            .ReadFromJsonAsync<List<SessionDto>>();

        var userBSession = Assert.Single(sessions!);

        // Act: User A tries to revoke User B's session
        var response = await userAClient.DeleteAsync(
            $"/api/v1/me/sessions/{userBSession.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Verify User B's session remains active
        using var scope = host.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

        var session = await db.RefreshTokens.SingleAsync(
            x => x.RefreshTokenId == userBSession.Id);

        Assert.Null(session.RevokedAt);
    }

    [Fact]
    public async Task Revoke_selected_session_returns_not_found_for_invalid_id()
    {
        // Arrange
        await using var host = new ApiHost();

        await host.SeedUserAsync("invalid-session@example.test", "USER");

        var client = await host.LoginClientAsync(
            "invalid-session@example.test");

        var invalidSessionId = Guid.NewGuid();

        // Act
        var response = await client.DeleteAsync(
            $"/api/v1/me/sessions/{invalidSessionId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    [Fact]
    public async Task Revoke_selected_session_twice_returns_conflict()
    {
        // Arrange
        await using var host = new ApiHost();

        await host.SeedUserAsync("revoke-twice@example.test", "USER");

        var currentClient = await host.LoginClientAsync(
            "revoke-twice@example.test");

        _ = await host.LoginClientAsync(
            "revoke-twice@example.test");

        var sessionsResponse = await currentClient.GetAsync(
            "/api/v1/me/sessions");

        var sessions = await sessionsResponse.Content
            .ReadFromJsonAsync<List<SessionDto>>();

        var selected = Assert.Single(
            sessions!,
            session => !session.IsCurrent);

        var url = $"/api/v1/me/sessions/{selected.Id}";

        // Act
        var firstResponse = await currentClient.DeleteAsync(url);
        var secondResponse = await currentClient.DeleteAsync(url);

        // Assert
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Revoke_others_twice_returns_conflict()
    {
        // Arrange
        await using var host = new ApiHost();

        await host.SeedUserAsync("revoke-others-twice@example.test", "USER");

        var currentClient = await host.LoginClientAsync(
            "revoke-others-twice@example.test");

        _ = await host.LoginClientAsync(
            "revoke-others-twice@example.test");

        // Act
        var firstResponse = await currentClient.DeleteAsync(
            "/api/v1/me/sessions/others");

        var secondResponse = await currentClient.DeleteAsync(
            "/api/v1/me/sessions/others");

        // Assert
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Revoke_others_ignores_expired_and_already_revoked_sessions()
    {
        // Arrange
        await using var host = new ApiHost();

        await host.SeedUserAsync("session-states@example.test", "USER");

        var currentClient = await host.LoginClientAsync(
            "session-states@example.test");

        var expiredClient = await host.LoginClientAsync(
            "session-states@example.test");

        var revokedClient = await host.LoginClientAsync(
            "session-states@example.test");

        var activeClient = await host.LoginClientAsync(
            "session-states@example.test");

        // Get each client's current session ID
        async Task<Guid> GetCurrentSessionId(HttpClient client)
        {
            var response = await client.GetAsync("/api/v1/me/sessions");

            response.EnsureSuccessStatusCode();

            var sessions = await response.Content
                .ReadFromJsonAsync<List<SessionDto>>();

            return Assert.Single(sessions!, x => x.IsCurrent).Id;
        }

        var expiredId = await GetCurrentSessionId(expiredClient);
        var revokedId = await GetCurrentSessionId(revokedClient);
        var activeId = await GetCurrentSessionId(activeClient);

        // Change database states
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<OrchestraDbContext>();

            var expiredSession = await db.RefreshTokens
                .SingleAsync(x => x.RefreshTokenId == expiredId);

            var revokedSession = await db.RefreshTokens
                .SingleAsync(x => x.RefreshTokenId == revokedId);

            expiredSession.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);

            revokedSession.RevokedAt = DateTime.UtcNow.AddMinutes(-2);
            revokedSession.RevokedReason = "previous revoke";

            await db.SaveChangesAsync();
        }

        // Act
        var response = await currentClient.DeleteAsync(
            "/api/v1/me/sessions/others");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RevokeResponse>();

        Assert.Equal(1, result?.RevokedCount);

        // Verify database states
        using var verifyScope = host.Services.CreateScope();

        var verifyDb = verifyScope.ServiceProvider
            .GetRequiredService<OrchestraDbContext>();

        var expired = await verifyDb.RefreshTokens
            .SingleAsync(x => x.RefreshTokenId == expiredId);

        var revoked = await verifyDb.RefreshTokens
            .SingleAsync(x => x.RefreshTokenId == revokedId);

        var active = await verifyDb.RefreshTokens
            .SingleAsync(x => x.RefreshTokenId == activeId);

        Assert.Null(expired.RevokedAt);

        Assert.NotNull(revoked.RevokedAt);
        Assert.Equal("previous revoke", revoked.RevokedReason);

        Assert.NotNull(active.RevokedAt);
        Assert.Equal("forced revoke", active.RevokedReason);
    }

    [Fact]
    public async Task Revoke_selected_session_concurrently_only_one_request_succeeds()
    {
        // Arrange
        await using var host = new ApiHost();

        await host.SeedUserAsync("concurrent-revoke@example.test", "USER");

        var currentClient = await host.LoginClientAsync(
            "concurrent-revoke@example.test");

        _ = await host.LoginClientAsync(
            "concurrent-revoke@example.test");

        var sessionsResponse = await currentClient.GetAsync(
            "/api/v1/me/sessions");

        var sessions = await sessionsResponse.Content
            .ReadFromJsonAsync<List<SessionDto>>();

        var selected = Assert.Single(
            sessions!,
            x => !x.IsCurrent);

        var url = $"/api/v1/me/sessions/{selected.Id}";

        // Act: Send two requests concurrently
        var request1 = currentClient.DeleteAsync(url);
        var request2 = currentClient.DeleteAsync(url);

        var responses = await Task.WhenAll(request1, request2);

        // Assert
        Assert.Single(
            responses,
            x => x.StatusCode == HttpStatusCode.OK);

        Assert.Single(
            responses,
            x => x.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Get_sessions_without_authentication_returns_unauthorized()
    {
        // Arrange
        await using var host = new ApiHost();

        using var client = host.CreateClient();

        // Act
        var response = await client.GetAsync(
            "/api/v1/me/sessions");

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }


    private sealed record MessageResponse(string Message);
    private sealed record RevokeResponse(string Message, int RevokedCount);
}
