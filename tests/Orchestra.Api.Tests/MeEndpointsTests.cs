using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Data;
using Orchestra.Api.Modules.Auth;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class MeEndpointsTests
{
    [Fact]
    public async Task Change_password_rejects_incorrect_current_password()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("wrong-password@example.test", "USER");
        var client = await host.LoginClientAsync("wrong-password@example.test");

        var response = await client.PostAsJsonAsync("/api/v1/me/password", new
        {
            currentPassword = "incorrect-password",
            newPassword = "new-long-passphrase-456"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Change_password_rejects_short_new_password()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("short-password@example.test", "USER");
        var client = await host.LoginClientAsync("short-password@example.test");

        var response = await client.PostAsJsonAsync("/api/v1/me/password", new
        {
            currentPassword = "long-passphrase-123",
            newPassword = "short"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Change_password_requires_authentication()
    {
        await using var host = new ApiHost();

        var response = await host.CreateClient().PostAsJsonAsync("/api/v1/me/password", new
        {
            currentPassword = "long-passphrase-123",
            newPassword = "new-long-passphrase-456"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Patch_me_updates_only_supplied_profile_fields()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("profile@example.test", "USER");
        var client = await host.LoginClientAsync("profile@example.test");

        var response = await client.PatchAsJsonAsync("/api/v1/me", new
        {
            firstName = "Updated",
            jobTitle = "Principal Engineer"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal("Updated", user?.FirstName);
        Assert.Equal("User", user?.LastName);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var stored = await db.AppUsers.SingleAsync(u => u.Email == "profile@example.test");
        Assert.Equal("Updated", stored.FirstName);
        Assert.Equal("User", stored.LastName);
        Assert.Equal("Principal Engineer", stored.JobTitle);
    }

    [Fact]
    public async Task Patch_me_rejects_empty_or_oversized_profile_updates()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("invalid-profile@example.test", "USER");
        var client = await host.LoginClientAsync("invalid-profile@example.test");

        var empty = await client.PatchAsJsonAsync("/api/v1/me", new { });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);

        var oversized = await client.PatchAsJsonAsync("/api/v1/me", new { firstName = new string('x', 101) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, oversized.StatusCode);
    }
}
