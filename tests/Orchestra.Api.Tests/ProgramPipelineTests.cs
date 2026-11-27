using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class ProgramPipelineTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Http_redirects_to_the_configured_https_port(string environment)
    {
        await using var baseHost = new ApiHost();
        await using var host = baseHost.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["HTTPS_PORT"] = "7235" }));
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost:5000"), AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/health/live?probe=true");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal(new Uri("https://localhost:7235/health/live?probe=true"), response.Headers.Location);
    }

    [Fact]
    public async Task Http_only_development_can_still_serve_requests()
    {
        await using var baseHost = new ApiHost();
        await using var host = baseHost.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["HTTPS_PORT"] = null, ["HTTPS_PORTS"] = null,
                    ["urls"] = "http://localhost:5000"
                })));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost:5000"), AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/change-password")]
    [InlineData("/api/v1/me/password")]
    public async Task Initial_password_can_be_changed_through_either_endpoint(string path)
    {
        await using var host = new ApiHost();
        await SeedPasswordChangeRequiredUserAsync(host);
        using var client = await host.LoginClientAsync("initial-password@example.test");

        using var response = await client.PostAsJsonAsync(path, new
        {
            currentPassword = "long-passphrase-123", newPassword = "new-long-passphrase-456"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        Assert.False((await db.AppUsers.SingleAsync()).MustChangePassword);
        using var session = await client.GetAsync("/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Password_change_requirement_does_not_block_health_checks(string path)
    {
        await using var host = new ApiHost();
        await SeedPasswordChangeRequiredUserAsync(host);
        using var client = await host.LoginClientAsync("initial-password@example.test");

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Password_change_requirement_still_blocks_other_api_actions()
    {
        await using var host = new ApiHost();
        await SeedPasswordChangeRequiredUserAsync(host);
        using var client = await host.LoginClientAsync("initial-password@example.test");

        using var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("PASSWORD_CHANGE_REQUIRED", (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    [Fact]
    public async Task Rate_limit_rejection_is_retryable_and_includes_retry_after()
    {
        await using var host = new ApiHost();
        using var client = host.CreateClient();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var accepted = await client.PostAsJsonAsync("/api/v1/auth/login", new { });
            Assert.Equal(HttpStatusCode.BadRequest, accepted.StatusCode);
        }

        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), response.Headers.RetryAfter?.Delta);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("RATE_LIMITED", error?.Code);
        Assert.True(error?.Retryable);
    }

    private static async Task SeedPasswordChangeRequiredUserAsync(ApiHost host)
    {
        await host.SeedUserAsync("initial-password@example.test", "USER");
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        (await db.AppUsers.SingleAsync()).MustChangePassword = true;
        await db.SaveChangesAsync();
    }
}
