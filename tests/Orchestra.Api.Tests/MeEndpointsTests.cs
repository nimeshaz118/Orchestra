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
