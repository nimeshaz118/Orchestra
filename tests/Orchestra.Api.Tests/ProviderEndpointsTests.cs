using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Modules.Providers;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class ProviderEndpointsTests
{
    private const string Url = "/api/v1/super-admin/providers";

    [Fact]
    public async Task Super_admin_creates_provider_and_can_list_and_get_it()
    {
        await using var host = new ApiHost();
        var client = await SuperAdminClientAsync(host);

        var create = await client.PostAsJsonAsync(Url, NewProvider());

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var provider = await create.Content.ReadFromJsonAsync<ProviderDto>();
        Assert.NotNull(provider);
        Assert.NotEqual(Guid.Empty, provider.ProviderId);
        Assert.Equal("NVIDIA", provider.ProviderName);
        Assert.Equal("https://integrate.api.nvidia.com/v1", provider.BaseUrl);
        Assert.Equal("NVIDIA_API_KEY", provider.CredentialReference);
        Assert.Equal("INACTIVE", provider.Status);
        Assert.Equal(60, provider.TimeoutSeconds);
        Assert.Equal("us", provider.Configuration.GetProperty("region").GetString());
        Assert.NotNull(create.Headers.Location);
        Assert.EndsWith($"/api/v1/super-admin/providers/{provider.ProviderId}",
            create.Headers.Location.ToString());

        var get = await client.GetAsync($"{Url}/{provider.ProviderId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(provider.ProviderId, (await get.Content.ReadFromJsonAsync<ProviderDto>())?.ProviderId);

        var list = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains((await list.Content.ReadFromJsonAsync<List<ProviderDto>>())!,
            item => item.ProviderId == provider.ProviderId);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var saved = await db.AiProviders.SingleAsync(item => item.ProviderId == provider.ProviderId);
        var actor = await db.AppUsers.SingleAsync(user => user.Email == "super@example.test");
        Assert.Equal(actor.UserId, saved.CreatedByUserId);
        Assert.Equal("INACTIVE", saved.Status);
        Assert.Contains(await db.AuditLogs.ToListAsync(),
            audit => audit.TargetId == provider.ProviderId && audit.Action == "PROVIDER_CREATED");
    }

    [Fact]
    public async Task Patch_changes_supplied_fields_and_preserves_omitted_fields()
    {
        await using var host = new ApiHost();
        var client = await SuperAdminClientAsync(host);
        var provider = await CreateProviderAsync(client);

        var response = await client.PatchAsJsonAsync($"{Url}/{provider.ProviderId}", new
        {
            providerName = "New NVIDIA",
            timeoutSeconds = 25,
            credentialReference = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProviderDto>();
        Assert.Equal("New NVIDIA", updated?.ProviderName);
        Assert.Equal(25, updated?.TimeoutSeconds);
        Assert.Null(updated?.CredentialReference);
        Assert.Equal(provider.BaseUrl, updated?.BaseUrl);
        Assert.Equal("INACTIVE", updated?.Status);
        Assert.Equal("us", updated?.Configuration.GetProperty("region").GetString());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var saved = await db.AiProviders.SingleAsync(item => item.ProviderId == provider.ProviderId);
        Assert.Equal("New NVIDIA", saved.ProviderName);
        Assert.Null(saved.CredentialReference);
        Assert.Equal(25, saved.TimeoutSeconds);
        Assert.Contains(await db.AuditLogs.ToListAsync(),
            audit => audit.TargetId == provider.ProviderId && audit.Action == "PROVIDER_UPDATED");
    }

    [Fact]
    public async Task Enable_and_disable_change_status_and_can_be_repeated()
    {
        await using var host = new ApiHost();
        var client = await SuperAdminClientAsync(host);
        var provider = await CreateProviderAsync(client);
        var path = $"{Url}/{provider.ProviderId}";

        foreach (var (action, expected) in new[]
        {
            ("enable", "ACTIVE"), ("enable", "ACTIVE"),
            ("disable", "INACTIVE"), ("disable", "INACTIVE")
        })
        {
            var response = await client.PostAsync($"{path}/{action}", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expected, (await response.Content.ReadFromJsonAsync<ProviderDto>())?.Status);
        }

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        Assert.Equal("INACTIVE", (await db.AiProviders.SingleAsync(p => p.ProviderId == provider.ProviderId)).Status);
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.TargetId == provider.ProviderId && a.Action == "PROVIDER_ENABLED"));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.TargetId == provider.ProviderId && a.Action == "PROVIDER_DISABLED"));
    }

    [Fact]
    public async Task Unknown_provider_returns_404_for_detail_update_and_status_changes()
    {
        await using var host = new ApiHost();
        var client = await SuperAdminClientAsync(host);
        var path = $"{Url}/{Guid.NewGuid()}";

        var responses = new[]
        {
            await client.GetAsync(path),
            await client.PatchAsJsonAsync(path, new { providerName = "Other" }),
            await client.PostAsync($"{path}/enable", null),
            await client.PostAsync($"{path}/disable", null)
        };
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("PROVIDER_NOT_FOUND", (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
        }
    }

    [Fact]
    public async Task Unauthenticated_and_non_super_admin_clients_cannot_manage_providers()
    {
        await using var host = new ApiHost();
        var anonymous = host.CreateClient();
        await host.SeedUserAsync("admin@example.test", "ADMIN");
        await host.SeedUserAsync("user@example.test", "USER");
        var admin = await host.LoginClientAsync("admin@example.test");
        var user = await host.LoginClientAsync("user@example.test");
        var id = Guid.NewGuid();

        foreach (var client in new[] { anonymous, admin, user })
        {
            var expected = client == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            Assert.Equal(expected, (await client.GetAsync(Url)).StatusCode);
            Assert.Equal(expected, (await client.PostAsJsonAsync(Url, NewProvider())).StatusCode);
            Assert.Equal(expected, (await client.GetAsync($"{Url}/{id}")).StatusCode);
            Assert.Equal(expected, (await client.PatchAsJsonAsync($"{Url}/{id}", new { providerName = "Other" })).StatusCode);
            Assert.Equal(expected, (await client.PostAsync($"{Url}/{id}/enable", null)).StatusCode);
            Assert.Equal(expected, (await client.PostAsync($"{Url}/{id}/disable", null)).StatusCode);
        }
    }

    [Theory]
    [InlineData(" ", "https://integrate.api.nvidia.com/v1", "providerName")]
    [InlineData("NVIDIA", "http://example.test/v1", "baseUrl")]
    [InlineData("NVIDIA", "https://example.test/v1?key=secret", "baseUrl")]
    public async Task Create_rejects_invalid_fields(string name, string baseUrl, string field)
    {
        await using var host = new ApiHost();
        var client = await SuperAdminClientAsync(host);

        var response = await client.PostAsJsonAsync(Url, new
        {
            providerName = name, baseUrl, credentialReference = "NVIDIA_API_KEY",
            timeoutSeconds = 60, configuration = new { }
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("INVALID_PROVIDER", error?.Code);
        Assert.Contains(field, error!.Errors!.Keys);
    }

    [Fact]
    public async Task Patch_rejects_empty_unknown_and_invalid_changes_without_modifying_provider()
    {
        await using var host = new ApiHost();
        var client = await SuperAdminClientAsync(host);
        var provider = await CreateProviderAsync(client);
        var path = $"{Url}/{provider.ProviderId}";

        foreach (var body in new object[]
        {
            new { }, new { status = "ACTIVE" }, new { timeoutSeconds = 0 }
        })
        {
            var response = await client.PatchAsJsonAsync(path, body);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("INVALID_PROVIDER", (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
        }

        var get = await client.GetAsync(path);
        var unchanged = await get.Content.ReadFromJsonAsync<ProviderDto>();
        Assert.Equal("INACTIVE", unchanged?.Status);
        Assert.Equal(60, unchanged?.TimeoutSeconds);
    }

    private static object NewProvider() => new
    {
        providerName = "NVIDIA",
        baseUrl = "https://integrate.api.nvidia.com/v1",
        credentialReference = "NVIDIA_API_KEY",
        timeoutSeconds = 60,
        configuration = new { region = "us" }
    };

    private static async Task<HttpClient> SuperAdminClientAsync(ApiHost host)
    {
        await host.SeedUserAsync("super@example.test", "SUPER_ADMIN");
        return await host.LoginClientAsync("super@example.test");
    }

    private static async Task<ProviderDto> CreateProviderAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(Url, NewProvider());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProviderDto>())!;
    }
}
