using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class LogoutAllTests
{
    [Fact]
    public async Task Logout_all_revokes_every_own_session_and_preserves_other_users()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("owner@example.test", "USER");
        await host.SeedUserAsync("other@example.test", "USER");
        using var first = await host.LoginClientAsync("owner@example.test");
        using var second = await host.LoginClientAsync("owner@example.test");
        using var other = await host.LoginClientAsync("other@example.test");

        var response = await first.PostAsync("/api/v1/auth/logout-all", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("orchestra_refresh=;"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/v1/me")).StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var owner = await db.AppUsers.SingleAsync(u => u.Email == "owner@example.test");
        var sessions = await db.RefreshTokens.Where(t => t.UserId == owner.UserId).ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => { Assert.NotNull(s.RevokedAt); Assert.Equal("LOGOUT_ALL", s.RevokedReason); });
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.PostAsync("/api/v1/auth/logout-all", null)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logout_all_requires_a_valid_bearer_even_with_a_refresh_cookie(bool forged)
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("owner@example.test", "USER");
        using var client = await host.LoginClientAsync("owner@example.test");
        client.DefaultRequestHeaders.Authorization = forged ? new AuthenticationHeaderValue("Bearer", "forged") : null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/logout-all", null)).StatusCode);
    }

    [Theory]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("missing", HttpStatusCode.Unauthorized)]
    [InlineData("disabled", HttpStatusCode.Forbidden)]
    [InlineData("deleted", HttpStatusCode.Unauthorized)]
    public async Task Logout_all_checks_current_user_and_session(string state, HttpStatusCode expected)
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("owner@example.test", "USER");
        using var client = await host.LoginClientAsync("owner@example.test");
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
            var user = await db.AppUsers.SingleAsync();
            var session = await db.RefreshTokens.SingleAsync();
            if (state == "expired") session.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            if (state == "revoked") session.RevokedAt = DateTime.UtcNow;
            if (state == "missing") db.RefreshTokens.Remove(session);
            if (state == "disabled") user.Status = "INACTIVE";
            if (state == "deleted") user.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        Assert.Equal(expected, (await client.PostAsync("/api/v1/auth/logout-all", null)).StatusCode);
    }

    [Fact]
    public async Task Logout_all_save_failure_returns_safe_500_and_keeps_sessions()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("owner@example.test", "USER");
        using var client = await host.LoginClientAsync("owner@example.test");
        host.FailSessionSave = true;
        var response = await client.PostAsync("/api/v1/auth/logout-all", null);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("INTERNAL_ERROR", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        Assert.Null((await db.RefreshTokens.SingleAsync()).RevokedAt);
    }
}
