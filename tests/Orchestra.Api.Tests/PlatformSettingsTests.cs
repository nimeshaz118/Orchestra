using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class PlatformSettingsTests
{
    private const string Url = "/api/v1/super-admin/platform-settings";

    [Fact]
    public async Task Super_admin_reads_settings_in_key_order_and_sensitive_values_are_redacted()
    {
        await using var host = new ApiHost();
        await SeedSettingAsync(host, "zeta.setting", """{"enabled":true}""", isSensitive: false);
        await SeedSettingAsync(host, "alpha.secret", """{"token":"secret-value"}""", isSensitive: true);
        await SeedSettingAsync(host, "alpha.setting", "42", isSensitive: false);
        var client = await SuperAdminClientAsync(host);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await response.Content.ReadFromJsonAsync<List<PlatformSettingResponse>>();
        Assert.NotNull(settings);
        Assert.Equal(
            new[] { "alpha.secret", "alpha.setting", "zeta.setting" },
            settings.Select(setting => setting.Key));
        Assert.True(settings[0].IsSensitive);
        Assert.Null(settings[0].Value);
        Assert.Equal(JsonValueKind.Number, settings[1].Value?.ValueKind);
        Assert.Equal(42, settings[1].Value?.GetInt32());
        Assert.Equal(JsonValueKind.Object, settings[2].Value?.ValueKind);
        Assert.True(settings[2].Value?.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Admin_and_unauthenticated_users_cannot_read_platform_settings()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("admin@example.test", "ADMIN");
        var admin = await host.LoginClientAsync("admin@example.test");
        var unauthenticated = host.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthenticated.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Super_admin_gets_empty_list_when_no_platform_settings_exist()
    {
        await using var host = new ApiHost();
        var client = await SuperAdminClientAsync(host);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<PlatformSettingResponse>>())!);
    }

    [Fact]
    public async Task Super_admin_creates_settings_and_sensitive_values_are_redacted()
    {
        await using var host = new ApiHost();
        var client = await SuperAdminClientAsync(host);

        var response = await client.PostAsJsonAsync(Url, new
        {
            key = "limits.max-users",
            value = new { count = 25 },
            description = "Maximum users per organization",
            isSensitive = false
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<PlatformSettingResponse>();
        Assert.Equal("limits.max-users", created?.Key);
        Assert.False(created?.IsSensitive);
        Assert.Equal(25, created?.Value?.GetProperty("count").GetInt32());

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
            var saved = await db.PlatformSettings.SingleAsync(setting => setting.SettingKey == "limits.max-users");
            var actor = await db.AppUsers.SingleAsync(user => user.Email == "super@example.test");
            Assert.Equal("{\"count\":25}", saved.SettingValue);
            Assert.Equal(actor.UserId, saved.UpdatedByUserId);
        }

        var sensitiveResponse = await client.PostAsJsonAsync(Url, new
        {
            key = "integration.secret",
            value = "secret-value",
            description = "Sensitive integration value",
            isSensitive = true
        });

        Assert.Equal(HttpStatusCode.Created, sensitiveResponse.StatusCode);
        var sensitive = await sensitiveResponse.Content.ReadFromJsonAsync<PlatformSettingResponse>();
        Assert.True(sensitive?.IsSensitive);
        Assert.Null(sensitive?.Value);
    }

    [Fact]
    public async Task Create_rejects_invalid_and_duplicate_settings()
    {
        await using var host = new ApiHost();
        var client = await SuperAdminClientAsync(host);

        var missingKey = await client.PostAsJsonAsync(Url,
            new { key = " ", value = 1, description = (string?)null, isSensitive = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missingKey.StatusCode);
        var missingKeyError = await missingKey.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("INVALID_PLATFORM_SETTING", missingKeyError?.Code);
        Assert.Contains("key", missingKeyError!.Errors!.Keys);

        var missingValue = await client.PostAsJsonAsync(Url,
            new { key = "limits.max-users", value = (string?)null, description = (string?)null, isSensitive = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missingValue.StatusCode);
        var missingValueError = await missingValue.Content.ReadFromJsonAsync<ApiError>();
        Assert.Contains("value", missingValueError!.Errors!.Keys);

        var longKey = await client.PostAsJsonAsync(Url,
            new { key = new string('k', 121), value = true, description = (string?)null, isSensitive = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, longKey.StatusCode);
        Assert.Contains("key", (await longKey.Content.ReadFromJsonAsync<ApiError>())!.Errors!.Keys);

        var created = await client.PostAsJsonAsync(Url,
            new { key = "limits.max-users", value = 25, description = (string?)null, isSensitive = false });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var duplicate = await client.PostAsJsonAsync(Url,
            new { key = "limits.max-users", value = 50, description = (string?)null, isSensitive = false });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var duplicateError = await duplicate.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("PLATFORM_SETTING_EXISTS", duplicateError?.Code);
        Assert.Contains("key", duplicateError!.Errors!.Keys);
    }

    [Fact]
    public async Task Admin_cannot_create_platform_settings()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("admin@example.test", "ADMIN");
        var client = await host.LoginClientAsync("admin@example.test");

        var response = await client.PostAsJsonAsync(Url,
            new { key = "limits.max-users", value = 25, description = (string?)null, isSensitive = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    [Fact]
    public async Task Super_admin_updates_an_existing_setting_and_preserves_omitted_fields()
    {
        await using var host = new ApiHost();
        await SeedSettingAsync(host, "limits.max-users", """{"count":25}""", isSensitive: false);
        var client = await SuperAdminClientAsync(host);

        var response = await client.PatchAsJsonAsync(Url, new
        {
            key = "limits.max-users",
            value = new { count = 50 }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<PlatformSettingResponse>();
        Assert.Equal(50, updated?.Value?.GetProperty("count").GetInt32());
        Assert.Equal("Description for limits.max-users", updated?.Description);
        Assert.False(updated?.IsSensitive);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var saved = await db.PlatformSettings.SingleAsync(setting => setting.SettingKey == "limits.max-users");
        var actor = await db.AppUsers.SingleAsync(user => user.Email == "super@example.test");
        Assert.Equal("{\"count\":50}", saved.SettingValue);
        Assert.Equal(actor.UserId, saved.UpdatedByUserId);
    }

    [Fact]
    public async Task Patch_can_clear_description_and_redacts_a_setting_marked_sensitive()
    {
        await using var host = new ApiHost();
        await SeedSettingAsync(host, "integration.secret", "\"old-value\"", isSensitive: false);
        var client = await SuperAdminClientAsync(host);

        var response = await client.PatchAsJsonAsync(Url, new
        {
            key = "integration.secret",
            description = (string?)null,
            isSensitive = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<PlatformSettingResponse>();
        Assert.Null(updated?.Description);
        Assert.True(updated?.IsSensitive);
        Assert.Null(updated?.Value);
    }

    [Fact]
    public async Task Patch_rejects_missing_settings_empty_changes_and_immutable_fields()
    {
        await using var host = new ApiHost();
        await SeedSettingAsync(host, "limits.max-users", "25", isSensitive: false);
        var client = await SuperAdminClientAsync(host);

        var missing = await client.PatchAsJsonAsync(Url, new { key = "unknown.setting", value = 5 });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("PLATFORM_SETTING_NOT_FOUND",
            (await missing.Content.ReadFromJsonAsync<ApiError>())?.Code);

        var empty = await client.PatchAsJsonAsync(Url, new { key = "limits.max-users" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        Assert.Contains("request",
            (await empty.Content.ReadFromJsonAsync<ApiError>())!.Errors!.Keys);

        var nullValue = await client.PatchAsJsonAsync(Url,
            new { key = "limits.max-users", value = (string?)null });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, nullValue.StatusCode);
        Assert.Contains("value",
            (await nullValue.Content.ReadFromJsonAsync<ApiError>())!.Errors!.Keys);

        var denied = await client.PatchAsJsonAsync(Url,
            new { key = "limits.max-users", updatedByUserId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, denied.StatusCode);
        Assert.Contains("updatedByUserId",
            (await denied.Content.ReadFromJsonAsync<ApiError>())!.Errors!.Keys);
    }

    [Fact]
    public async Task Admin_cannot_update_platform_settings()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("admin@example.test", "ADMIN");
        var client = await host.LoginClientAsync("admin@example.test");

        var response = await client.PatchAsJsonAsync(Url,
            new { key = "limits.max-users", value = 50 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    private static async Task SeedSettingAsync(
        ApiHost host, string key, string value, bool isSensitive)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        db.PlatformSettings.Add(new PlatformSetting
        {
            SettingKey = key,
            SettingValue = value,
            Description = $"Description for {key}",
            IsSensitive = isSensitive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<HttpClient> SuperAdminClientAsync(ApiHost host)
    {
        await host.SeedUserAsync("super@example.test", "SUPER_ADMIN");
        return await host.LoginClientAsync("super@example.test");
    }

    private sealed record PlatformSettingResponse(
        string Key, JsonElement? Value, string? Description, bool IsSensitive);
}
