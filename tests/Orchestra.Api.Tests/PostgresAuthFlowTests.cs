using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orchestra.Api.Common;
using Orchestra.Api.Modules.Auth;
using Orchestra.Api.Modules.Catalog;
using Orchestra.Api.Modules.Departments;
using Orchestra.Api.Modules.Domains;
using Orchestra.Api.Modules.FeatureFlags;
using Orchestra.Api.Modules.Maintenance;
using Orchestra.Api.Modules.Providers;
using Orchestra.Api.Modules.Users;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class PostgresAuthFlowTests
{
    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public Task Maintenance_transitions_and_audits_are_shared_by_two_api_hosts() => WithDatabaseAsync(async connection =>
    {
        const string startPath = "/api/v1/super-admin/maintenance/start";
        const string endPath = "/api/v1/super-admin/maintenance/end";
        var settings = new Dictionary<string, string?>
        {
            ["AiService:BaseUrl"] = "",
            ["AiService:ServiceToken"] = ""
        };
        using var baseHost = new PostgresHost(connection);
        using var firstHost = baseHost.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(settings)));
        using var secondHost = baseHost.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(settings)));
        var clientOptions = new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") };
        using var first = firstHost.CreateClient(clientOptions);
        using var second = secondHost.CreateClient(clientOptions);

        const string email = "super@example.test";
        const string initialPassword = "initial-super-passphrase";
        const string newPassword = "changed-super-passphrase";
        async Task<AuthenticationHeaderValue> SignInAsync(string password)
        {
            using var response = await first.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
            response.EnsureSuccessStatusCode();
            return new AuthenticationHeaderValue("Bearer",
                (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        }

        first.DefaultRequestHeaders.Authorization = await SignInAsync(initialPassword);
        using var change = await first.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = initialPassword, newPassword });
        change.EnsureSuccessStatusCode();
        first.DefaultRequestHeaders.Authorization = await SignInAsync(newPassword);
        second.DefaultRequestHeaders.Authorization = first.DefaultRequestHeaders.Authorization;

        const string settingsPath = "/api/v1/super-admin/platform-settings";
        var settingsList = (await first.GetFromJsonAsync<List<PlatformSettingDto>>(settingsPath))!;
        Assert.DoesNotContain(settingsList, setting => setting.Key == "system.maintenance");
        using var edit = await first.PatchAsJsonAsync(settingsPath, new { key = "system.maintenance", value = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, edit.StatusCode);
        using var apply = await first.PostAsJsonAsync(settingsPath + "/apply",
            new { key = "system.maintenance", action = "REVOKE" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, apply.StatusCode);

        var input = new StartMaintenanceRequest("Test concurrency", "A scheduled update is running.",
            DateTimeOffset.Parse("2028-02-01T15:00:00.1234567+05:30"), "authenticated_operations", "allow_finish");
        using var firstStart = new HttpRequestMessage(HttpMethod.Post, startPath) { Content = JsonContent.Create(input) };
        using var secondStart = new HttpRequestMessage(HttpMethod.Post, startPath) { Content = JsonContent.Create(input) };
        firstStart.Headers.Add("Idempotency-Key", "shared-postgres-start");
        secondStart.Headers.Add("Idempotency-Key", "shared-postgres-start");
        var starts = await Task.WhenAll(first.SendAsync(firstStart), second.SendAsync(secondStart));
        using var started = starts[0];
        using var replayed = starts[1];
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        var session = (await started.Content.ReadFromJsonAsync<MaintenanceStarted>())!;
        Assert.Equal(session, await replayed.Content.ReadFromJsonAsync<MaintenanceStarted>());

        using var startConflict = await second.PostAsJsonAsync(startPath, input);
        Assert.Equal(HttpStatusCode.Conflict, startConflict.StatusCode);
        Assert.Equal("MAINTENANCE_ALREADY_ACTIVE", (await startConflict.Content.ReadFromJsonAsync<ApiError>())?.Code);
        using var changedRequest = new HttpRequestMessage(HttpMethod.Post, startPath)
        {
            Content = JsonContent.Create(input with { Reason = "Changed reason" })
        };
        changedRequest.Headers.Add("Idempotency-Key", "shared-postgres-start");
        using var changed = await second.SendAsync(changedRequest);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("IDEMPOTENCY_CONFLICT", (await changed.Content.ReadFromJsonAsync<ApiError>())?.Code);
        using var blocked = await second.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        Assert.True((await second.GetFromJsonAsync<SystemStatus>("/api/v1/system/status"))!.MaintenanceActive);

        var outcome = new EndMaintenanceRequest("The concurrent maintenance test finished.");
        var ends = await Task.WhenAll(first.PostAsJsonAsync(endPath, outcome), second.PostAsJsonAsync(endPath, outcome));
        using var ended = Assert.Single(ends, response => response.StatusCode == HttpStatusCode.OK);
        using var endConflict = Assert.Single(ends, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("MAINTENANCE_NOT_ACTIVE", (await endConflict.Content.ReadFromJsonAsync<ApiError>())?.Code);
        var result = (await ended.Content.ReadFromJsonAsync<MaintenanceEnded>())!;
        Assert.Equal(session.MaintenanceId, result.MaintenanceId);
        Assert.Equal(new[] { "authenticated_api" }, result.RestoredServices);
        Assert.Equal(new[] { "ai" }, result.UnavailableServices);
        using var resumed = await second.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.False((await second.GetFromJsonAsync<SystemStatus>("/api/v1/system/status"))!.MaintenanceActive);
        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, startPath) { Content = JsonContent.Create(input) };
        replayRequest.Headers.Add("Idempotency-Key", "shared-postgres-start");
        using var replayAfterEnd = await second.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.OK, replayAfterEnd.StatusCode);
        Assert.Equal(session, await replayAfterEnd.Content.ReadFromJsonAsync<MaintenanceStarted>());
        Assert.False((await second.GetFromJsonAsync<SystemStatus>("/api/v1/system/status"))!.MaintenanceActive);

        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var audit = new NpgsqlCommand("""
            SELECT action FROM ai_orchestration.audit_log
            WHERE target_type = 'maintenance_session' AND target_id = $1
            ORDER BY occurred_at;
            """, db);
        audit.Parameters.Add(new NpgsqlParameter { Value = session.MaintenanceId });
        await using var rows = await audit.ExecuteReaderAsync();
        Assert.True(await rows.ReadAsync());
        Assert.Equal("MAINTENANCE_STARTED", rows.GetString(0));
        Assert.True(await rows.ReadAsync());
        Assert.Equal("MAINTENANCE_ENDED", rows.GetString(0));
        Assert.False(await rows.ReadAsync());
    });

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public Task Super_admin_can_test_providers_and_models_loaded_from_stored_procedures() => WithDatabaseAsync(async connection =>
    {
        var providerId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        await using (var db = new NpgsqlConnection(connection))
        {
            await db.OpenAsync();
            await using var seed = new NpgsqlCommand("""
                WITH provider AS (
                    INSERT INTO ai_orchestration.ai_provider
                        (provider_id, provider_name, base_url, credential_reference, status, timeout_seconds, configuration)
                    VALUES ($1, 'NVIDIA fixture', 'https://provider.example.test/catalog/v1',
                        'CATALOG_API_KEY', 'INACTIVE', 30,
                        '{"adapter":"nvidia","testText":"Database probe text","testMaxOutputTokens":5,"unrelated":"ignored"}')
                    RETURNING provider_id
                )
                INSERT INTO ai_orchestration.ai_model
                    (model_id, provider_id, provider_model_key, display_name, model_type, status, quality_tier)
                SELECT $2, provider_id, 'test/chat', 'Chat', 'CHAT', 'INACTIVE', 1 FROM provider;
                """, db);
            seed.Parameters.Add(new NpgsqlParameter { Value = providerId });
            seed.Parameters.Add(new NpgsqlParameter { Value = modelId });
            await seed.ExecuteNonQueryAsync();
        }

        using var diagnostics = new DiagnosticsHandler();
        using var baseHost = new PostgresHost(connection);
        using var host = baseHost.WithWebHostBuilder(builder => builder
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiService:BaseUrl"] = "https://ai-service.example.test",
                ["AiService:ServiceToken"] = "test-service-token"
            }))
            .ConfigureServices(services => services.AddHttpClient<AiDiagnosticsClient>()
                .ConfigurePrimaryHttpMessageHandler(() => diagnostics)));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var providerPath = $"/api/v1/super-admin/providers/{providerId}/test";
        var healthPath = $"/api/v1/super-admin/providers/{providerId}/health";
        var modelPath = $"/api/v1/super-admin/models/{modelId}/test";
        foreach (var path in new[] { providerPath, modelPath })
        {
            using var anonymous = await client.PostAsync(path, null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
        using var anonymousHealth = await client.GetAsync(healthPath);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousHealth.StatusCode);

        async Task SignInAsync(string email, string password)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
            login.EnsureSuccessStatusCode();
            var session = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        }

        async Task InitializePasswordAsync(string email, string initialPassword, string newPassword)
        {
            await SignInAsync(email, initialPassword);
            using var change = await client.PostAsJsonAsync("/api/v1/auth/change-password",
                new { currentPassword = initialPassword, newPassword });
            change.EnsureSuccessStatusCode();
            await SignInAsync(email, newPassword);
        }

        await InitializePasswordAsync("admin@example.test", "initial-admin-passphrase", "changed-admin-passphrase");
        foreach (var path in new[] { providerPath, modelPath })
        {
            using var forbidden = await client.PostAsync(path, null);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        using var forbiddenHealth = await client.GetAsync(healthPath);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenHealth.StatusCode);

        await InitializePasswordAsync("super@example.test", "initial-super-passphrase", "changed-super-passphrase");
        foreach (var (resource, code) in new[] { ("providers", "PROVIDER_NOT_FOUND"), ("models", "MODEL_NOT_FOUND") })
        {
            using var missing = await client.PostAsync($"/api/v1/super-admin/{resource}/{Guid.NewGuid()}/test", null);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Contains(code, await missing.Content.ReadAsStringAsync());
        }
        using var missingHealth = await client.GetAsync($"/api/v1/super-admin/providers/{Guid.NewGuid()}/health");
        Assert.Equal(HttpStatusCode.NotFound, missingHealth.StatusCode);
        Assert.Equal("PROVIDER_NOT_FOUND", (await missingHealth.Content.ReadFromJsonAsync<ApiError>())?.Code);
        var emptyHealth = (await client.GetFromJsonAsync<ProviderHealthDto>(healthPath))!;
        Assert.Equal("unknown", emptyHealth.CurrentStatus);
        Assert.Null(emptyHealth.AvailabilityPercent24h);
        Assert.Null(emptyHealth.LastCheckedAt);
        Assert.Empty(diagnostics.Requests);

        foreach (var path in new[] { providerPath, modelPath })
        {
            using var response = await client.PostAsync(path, null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<CatalogTestResult>())!;
            Assert.True(result.Success);
            Assert.True(result.LatencyMs >= 0);
        }

        Assert.Collection(diagnostics.Requests,
            provider =>
            {
                Assert.Equal(JsonValueKind.Null, provider.GetProperty("modelKey").ValueKind);
                Assert.Equal(JsonValueKind.Null, provider.GetProperty("modelType").ValueKind);
                Assert.Equal(JsonValueKind.Null, provider.GetProperty("testText").ValueKind);
            },
            model =>
            {
                Assert.Equal("test/chat", model.GetProperty("modelKey").GetString());
                Assert.Equal("CHAT", model.GetProperty("modelType").GetString());
                Assert.Equal("nvidia", model.GetProperty("adapter").GetString());
                Assert.Equal("Database probe text", model.GetProperty("testText").GetString());
                Assert.Equal(5, model.GetProperty("testMaxOutputTokens").GetInt32());
            });
        Assert.All(diagnostics.Requests, request =>
        {
            Assert.Equal("https://provider.example.test/catalog/v1", request.GetProperty("baseUrl").GetString());
            Assert.Equal("CATALOG_API_KEY", request.GetProperty("credentialReference").GetString());
            Assert.Equal(30, request.GetProperty("timeoutSeconds").GetInt32());
            Assert.False(request.TryGetProperty("configuration", out _));
        });

        foreach (var (upstream, expected, code, retryable) in new[]
        {
            (HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity, "INVALID_TEST_CONFIGURATION", false),
            (HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, "CATALOG_TEST_FAILED", false),
            (HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, "PROVIDER_UNAVAILABLE", true),
            (HttpStatusCode.GatewayTimeout, HttpStatusCode.GatewayTimeout, "CATALOG_TEST_TIMEOUT", true),
            (HttpStatusCode.Unauthorized, HttpStatusCode.ServiceUnavailable, "AI_SERVICE_UNAVAILABLE", true)
        })
        {
            diagnostics.Status = upstream;
            using var response = await client.PostAsync(modelPath, null);
            Assert.Equal(expected, response.StatusCode);
            var error = (await response.Content.ReadFromJsonAsync<ApiError>())!;
            Assert.Equal(code, error.Code);
            Assert.Equal(retryable, error.Retryable);
            Assert.DoesNotContain("private upstream details", error.Message);
        }

        var healthy = (await client.GetFromJsonAsync<ProviderHealthDto>(healthPath))!;
        Assert.Equal(providerId, healthy.ProviderId);
        Assert.Equal("healthy", healthy.CurrentStatus);
        Assert.Equal(100.0, healthy.AvailabilityPercent24h);
        Assert.NotNull(healthy.AverageLatencyMs24h);
        Assert.Equal(healthy.LastSuccessfulCheckAt, healthy.LastCheckedAt);
        Assert.Empty(healthy.RecentSafeErrorCategories);

        diagnostics.Status = HttpStatusCode.GatewayTimeout;
        using var timeout = await client.PostAsync(providerPath, null);
        Assert.Equal(HttpStatusCode.GatewayTimeout, timeout.StatusCode);

        // A private service error is recorded, but it gives no evidence about the provider.
        diagnostics.Status = HttpStatusCode.Unauthorized;
        using var diagnosticsUnavailable = await client.PostAsync(providerPath, null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, diagnosticsUnavailable.StatusCode);
        var probeCount = diagnostics.Requests.Count;
        var unavailable = (await client.GetFromJsonAsync<ProviderHealthDto>(healthPath))!;
        Assert.Equal("unavailable", unavailable.CurrentStatus);
        Assert.Equal(50.0, unavailable.AvailabilityPercent24h);
        Assert.Equal(healthy.AverageLatencyMs24h, unavailable.AverageLatencyMs24h);
        Assert.Equal(healthy.LastSuccessfulCheckAt, unavailable.LastSuccessfulCheckAt);
        Assert.True(unavailable.LastCheckedAt > unavailable.LastSuccessfulCheckAt);
        Assert.Equal(new[] { "AI_SERVICE_UNAVAILABLE", "CATALOG_TEST_TIMEOUT" }, unavailable.RecentSafeErrorCategories);
        Assert.Equal(probeCount, diagnostics.Requests.Count);

        const string providersPath = "/api/v1/super-admin/providers";
        using var second = await client.PostAsJsonAsync(providersPath, new
        {
            providerName = "Same endpoint, second key", baseUrl = "https://provider.example.test/catalog/v1",
            credentialReference = "CATALOG_API_KEY_2"
        });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var secondProvider = (await second.Content.ReadFromJsonAsync<ProviderDto>())!;
        diagnostics.Status = HttpStatusCode.NoContent;
        using var secondTest = await client.PostAsync($"{providersPath}/{secondProvider.ProviderId}/test", null);
        Assert.Equal(HttpStatusCode.OK, secondTest.StatusCode);
        Assert.Equal("CATALOG_API_KEY_2", diagnostics.Requests.Last().GetProperty("credentialReference").GetString());
    });

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public Task Stored_procedure_endpoints_preserve_scoping_and_session_revocation() => WithDatabaseAsync(async connection =>
    {
        await using (var db = new NpgsqlConnection(connection))
        {
            await db.OpenAsync();
            await using var seed = new NpgsqlCommand("""
                INSERT INTO ai_orchestration.department(department_name, description, status)
                VALUES ('Zoology', NULL, 'ACTIVE'), ('Accounting', 'Closed', 'INACTIVE'), ('Biology', NULL, 'ACTIVE');
                INSERT INTO ai_orchestration.approved_email_domain(domain_name, is_active)
                VALUES ('z.example.test', TRUE), ('a.example.test', FALSE);
                """, db);
            await seed.ExecuteNonQueryAsync();
        }

        using var host = new PostgresHost(connection);
        using var first = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var second = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var publicDepartments = (await first.GetFromJsonAsync<List<DepartmentDto>>("/api/v1/public/departments"))!;
        Assert.Equal(new[] { "Biology", "Zoology" }, publicDepartments.Select(d => d.Name));

        async Task SignInAsync(HttpClient client, string password)
        {
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "super@example.test", password });
            login.EnsureSuccessStatusCode();
            var session = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        }

        await SignInAsync(first, "initial-super-passphrase");
        var sessionUser = (await first.GetFromJsonAsync<UserDto>("/api/v1/auth/session"))!;
        Assert.True(sessionUser.MustChangePassword);
        Assert.Equal("SUPER_ADMIN", sessionUser.Role);
        using var initialChange = await first.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = "initial-super-passphrase", newPassword = "changed-super-passphrase" });
        initialChange.EnsureSuccessStatusCode();
        await SignInAsync(first, "changed-super-passphrase");
        await SignInAsync(second, "changed-super-passphrase");

        using (var createdSetting = await first.PostAsJsonAsync("/api/v1/super-admin/platform-settings",
            new
            {
                key = "integration.maintenance-mode",
                value = true,
                description = "Enable maintenance mode",
                isSensitive = false
            }))
        {
            Assert.Equal(HttpStatusCode.Created, createdSetting.StatusCode);
            var created = (await createdSetting.Content.ReadFromJsonAsync<PlatformSettingDto>())!;
            Assert.Equal("integration.maintenance-mode", created.Key);
            Assert.False(created.IsApplied);
            Assert.True(created.Value!.Value.GetBoolean());

            var effectiveBeforeApply = (await first.GetFromJsonAsync<List<PlatformSettingDto>>(
                "/api/v1/super-admin/platform-settings"))!;
            Assert.Contains(effectiveBeforeApply, setting =>
                setting.Key == "integration.maintenance-mode" && !setting.IsApplied);

            using var duplicateSetting = await first.PostAsJsonAsync(
                "/api/v1/super-admin/platform-settings",
                new { key = "integration.maintenance-mode", value = false, isSensitive = false });
            Assert.Equal(HttpStatusCode.Conflict, duplicateSetting.StatusCode);

            using var apply = await first.PostAsJsonAsync(
                "/api/v1/super-admin/platform-settings/apply",
                new { key = created.Key, action = "APPLY" });
            Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
            Assert.True((await apply.Content.ReadFromJsonAsync<PlatformSettingDto>())!.IsApplied);
        }

        using (var duplicateSetting = await first.PostAsJsonAsync("/api/v1/super-admin/platform-settings",
            new { key = "integration.maintenance-mode", value = false, isSensitive = false }))
            Assert.Equal(HttpStatusCode.Conflict, duplicateSetting.StatusCode);

        using (var updatedSetting = await first.PatchAsJsonAsync("/api/v1/super-admin/platform-settings",
            new
            {
                key = "integration.maintenance-mode",
                value = false,
                description = (string?)null,
                isSensitive = true
            }))
        {
            Assert.Equal(HttpStatusCode.OK, updatedSetting.StatusCode);
            var updated = (await updatedSetting.Content.ReadFromJsonAsync<PlatformSettingDto>())!;
            Assert.True(updated.IsSensitive);
            Assert.Null(updated.Value);
            Assert.Null(updated.Description);
            Assert.True(updated.IsApplied);
        }

        using (var missingSetting = await first.PatchAsJsonAsync("/api/v1/super-admin/platform-settings",
            new { key = "integration.missing", value = true }))
            Assert.Equal(HttpStatusCode.NotFound, missingSetting.StatusCode);

        var platformSettings = (await first.GetFromJsonAsync<List<PlatformSettingDto>>(
            "/api/v1/super-admin/platform-settings"))!;
        Assert.Null(platformSettings.Single(setting =>
            setting.Key == "integration.maintenance-mode").Value);

        await using (var settingsDb = new NpgsqlConnection(connection))
        {
            await settingsDb.OpenAsync();
            await using var saved = new NpgsqlCommand(
                "SELECT setting_value::text, description, is_sensitive, updated_by_user_id, is_applied " +
                "FROM ai_orchestration.platform_setting WHERE setting_key = $1",
                settingsDb);
            saved.Parameters.Add(new NpgsqlParameter { Value = "integration.maintenance-mode" });
            await using var reader = await saved.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("false", reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.GetBoolean(2));
            Assert.Equal(sessionUser.Id, reader.GetGuid(3));
            Assert.True(reader.GetBoolean(4));
        }

        var departments = (await first.GetFromJsonAsync<List<DepartmentDto>>("/api/v1/super-admin/departments"))!;
        Assert.Equal(new[] { "Accounting", "Biology", "Zoology" }, departments.Select(d => d.Name));
        var inactive = departments[0];
        Assert.Equal("INACTIVE", inactive.Status);
        Assert.Equal(inactive, await first.GetFromJsonAsync<DepartmentDto>($"/api/v1/super-admin/departments/{inactive.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync($"/api/v1/super-admin/departments/{Guid.NewGuid()}")).StatusCode);
        var domains = (await first.GetFromJsonAsync<List<DomainDto>>("/api/v1/admin/domains"))!;
        Assert.Equal(new[] { "a.example.test", "z.example.test" }, domains.Select(d => d.DomainName));
        Assert.False(domains[0].IsActive);

        var profile = (await first.GetFromJsonAsync<MeDto>("/api/v1/me"))!;
        Assert.Equal(sessionUser.Id, profile.Id);
        Assert.False(profile.MustChangePassword);
        Assert.Null(profile.DepartmentId);
        Assert.Empty((await first.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/v1/me/preferences"))!);
        using var savedPreferences = await first.PatchAsJsonAsync("/api/v1/me/preferences", new { layout = new { compact = true } });
        savedPreferences.EnsureSuccessStatusCode();
        var preferences = (await second.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/v1/me/preferences"))!;
        Assert.True(preferences["layout"].GetProperty("compact").GetBoolean());

        Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync("/api/v1/me/avatar")).StatusCode);
        await using (var db = new NpgsqlConnection(connection))
        {
            await db.OpenAsync();
            await using var avatar = new NpgsqlCommand(
                "UPDATE ai_orchestration.app_user SET avatar_data = decode('010203', 'hex') WHERE user_id = $1", db);
            avatar.Parameters.Add(new NpgsqlParameter { Value = profile.Id });
            await avatar.ExecuteNonQueryAsync();
        }
        Assert.Equal(new byte[] { 1, 2, 3 }, await first.GetByteArrayAsync("/api/v1/me/avatar"));

        var sessions = (await first.GetFromJsonAsync<List<SessionDto>>("/api/v1/me/sessions"))!;
        Assert.Equal(2, sessions.Count);
        Assert.False(sessions[0].IsCurrent);
        Assert.True(sessions[1].IsCurrent);
        using var wrongPassword = await first.PostAsJsonAsync("/api/v1/me/password",
            new { currentPassword = "wrong-current-password", newPassword = "another-super-passphrase" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);
        using var passwordChange = await first.PostAsJsonAsync("/api/v1/me/password",
            new { currentPassword = "changed-super-passphrase", newPassword = "another-super-passphrase" });
        passwordChange.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/v1/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
        await SignInAsync(first, "another-super-passphrase");
    });

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public Task Super_admin_can_list_only_non_sensitive_boolean_feature_flags() => WithDatabaseAsync(async connection =>
    {
        const string url = "/api/v1/super-admin/feature-flags";
        using var host = new PostgresHost(connection);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var anonymous = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        async Task SignInAsync(string email, string password)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
            login.EnsureSuccessStatusCode();
            var session = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        }

        async Task SignInAfterPasswordChangeAsync(string email, string currentPassword, string newPassword)
        {
            await SignInAsync(email, currentPassword);
            using var change = await client.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword, newPassword });
            change.EnsureSuccessStatusCode();
            await SignInAsync(email, newPassword);
        }

        await SignInAfterPasswordChangeAsync("admin@example.test", "initial-admin-passphrase", "changed-admin-passphrase");
        using var admin = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);

        await SignInAfterPasswordChangeAsync("super@example.test", "initial-super-passphrase", "changed-super-passphrase");
        Assert.Empty((await client.GetFromJsonAsync<List<FeatureFlagDto>>(url))!);

        await using (var db = new NpgsqlConnection(connection))
        {
            await db.OpenAsync();
            await using var seed = new NpgsqlCommand("""
                INSERT INTO ai_orchestration.platform_setting (setting_key, setting_value, description, is_sensitive, updated_at)
                VALUES ('feature_flag.zeta', 'false', NULL, FALSE, '2026-01-01 00:00:00+00'),
                       ('feature_flag.alpha', 'true', 'Alpha feature', FALSE, '2026-01-01 00:00:00+00'),
                       ('feature_flag.secret', 'true', 'Sensitive flag', TRUE, '2026-01-01 00:00:00+00'),
                       ('feature_flag.invalid', '"true"', NULL, FALSE, '2026-01-01 00:00:00+00'),
                       ('feature_flag.', 'true', NULL, FALSE, '2026-01-01 00:00:00+00'),
                       ('featureXflag.unrelated', 'true', NULL, FALSE, '2026-01-01 00:00:00+00');
                """, db);
            await seed.ExecuteNonQueryAsync();
        }

        var flags = await client.GetFromJsonAsync<List<FeatureFlagDto>>(url);
        var updatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new[]
        {
            new FeatureFlagDto("alpha", true, "Alpha feature", updatedAt),
            new FeatureFlagDto("zeta", false, null, updatedAt)
        }, flags);
    });

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public Task Verification_refresh_reuse_and_password_reset_are_single_use() => WithDatabaseAsync(async connection =>
        {
            using var host = new PostgresHost(connection);
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
            var department = await client.PostAsJsonAsync("/api/v1/super-admin/departments",
                new { name = "Not authorized" });
            Assert.Equal(HttpStatusCode.Unauthorized, department.StatusCode);

            var initialAdmin = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "admin@example.test", password = "initial-admin-passphrase" });
            Assert.Equal(HttpStatusCode.OK, initialAdmin.StatusCode);
            var initialAdminToken = (await initialAdmin.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", initialAdminToken);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/v1/admin/departments")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/change-password",
                new { currentPassword = "initial-admin-passphrase", newPassword = "changed-admin-passphrase" })).StatusCode);
            client.DefaultRequestHeaders.Authorization = null;
            var adminLogin = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "admin@example.test", password = "changed-admin-passphrase" });
            Assert.Equal(HttpStatusCode.OK, adminLogin.StatusCode);
            var adminToken = (await adminLogin.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/admin/departments")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/v1/super-admin/departments")).StatusCode);

            client.DefaultRequestHeaders.Authorization = null;
            var initialSuper = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "super@example.test", password = "initial-super-passphrase" });
            Assert.Equal(HttpStatusCode.OK, initialSuper.StatusCode);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                (await initialSuper.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/change-password",
                new { currentPassword = "initial-super-passphrase", newPassword = "changed-super-passphrase" })).StatusCode);
            client.DefaultRequestHeaders.Authorization = null;
            var superLogin = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "super@example.test", password = "changed-super-passphrase" });
            Assert.Equal(HttpStatusCode.OK, superLogin.StatusCode);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                (await superLogin.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/admin/departments")).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/super-admin/departments",
                new { name = "Finance", description = "Finance team" })).StatusCode);
            client.DefaultRequestHeaders.Authorization = null;

            await using (var database = new NpgsqlConnection(connection))
            {
                await database.OpenAsync();
                await using var seed = new NpgsqlCommand(
                    "INSERT INTO ai_orchestration.department(department_name) VALUES ('Engineering') RETURNING department_id", database);
                var departmentId = (Guid)(await seed.ExecuteScalarAsync())!;
                var registration = await client.PostAsJsonAsync("/api/v1/auth/register", new
                {
                    email = "integration@example.test", password = "long-passphrase-123",
                    firstName = "Integration", lastName = "User", departmentId
                });
                Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
            }
           

            // Confirm the initial registration email was generated.
            Assert.Single(host.Email.Sent);

            // Move the initial code's creation time back by 61 seconds.
            // This simulates waiting for the resend cooldown.
            await using (var resendConnection = new NpgsqlConnection(connection))
            {
                await resendConnection.OpenAsync();

                await using var command = new NpgsqlCommand("""
                    UPDATE ai_orchestration.verification_code
                    SET created_at = created_at - INTERVAL '61 seconds'
                    WHERE user_id = (
                        SELECT user_id
                        FROM ai_orchestration.app_user
                        WHERE email = 'integration@example.test'
                    )
                    AND purpose = 'EMAIL_VERIFICATION'
                    """, resendConnection);

                await command.ExecuteNonQueryAsync();
            }

            // Request another verification email.
            var resendResponse = await client.PostAsJsonAsync(
                "/api/v1/auth/resend-verification",
                new { email = "integration@example.test" });

            // Successful resend returns 200.
            Assert.Equal(
                HttpStatusCode.OK,
                resendResponse.StatusCode);

            // Two verification emails should now be recorded.
            Assert.Equal(2, host.Email.Sent.Count);

            // Use the newly issued code for the existing verification test.
            var verifyCode = host.Email.Sent.Last().Code;
                                

            //var verifyCode = Assert.Single(host.Email.Sent).Code;
            
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/verify-email",
                new { email = "integration@example.test", code = verifyCode })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/auth/verify-email",
                new { email = "integration@example.test", code = verifyCode })).StatusCode);

            var login = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "integration@example.test", password = "long-passphrase-123" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var oldCookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            var oldAccess = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
            // Upgrade a previously applied separate-table version without losing its image.
            await using (var upgradeConnection = new NpgsqlConnection(connection))
            {
                await upgradeConnection.OpenAsync();
                await using var oldTable = new NpgsqlCommand("""
                    CREATE TABLE ai_orchestration.user_avatar (
                        user_id uuid PRIMARY KEY REFERENCES ai_orchestration.app_user(user_id),
                        image_data bytea NOT NULL);
                    INSERT INTO ai_orchestration.user_avatar
                    SELECT user_id, decode('010203', 'hex') FROM ai_orchestration.app_user
                    WHERE email = 'integration@example.test';
                    """, upgradeConnection);
                await oldTable.ExecuteNonQueryAsync();
                var upgradeSql = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(FindSchemaFile())!, "002_user_avatar.sql"));
                await using var upgrade = new NpgsqlCommand(upgradeSql, upgradeConnection);
                await upgrade.ExecuteNonQueryAsync();
                await upgrade.ExecuteNonQueryAsync(); // Safe to rerun after the column exists.
                await using var copied = new NpgsqlCommand("SELECT avatar_data FROM ai_orchestration.app_user WHERE email = 'integration@example.test'", upgradeConnection);
                Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])(await copied.ExecuteScalarAsync())!);
                await using var removed = new NpgsqlCommand("SELECT to_regclass('ai_orchestration.user_avatar')::text", upgradeConnection);
                Assert.Equal(DBNull.Value, await removed.ExecuteScalarAsync());
            }
            // A new API host reads the same bytes from PostgreSQL after upload.
            using (var avatarHost = new PostgresHost(connection))
            using (var avatarClient = avatarHost.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }))
            {
                avatarClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", oldAccess);
                using var bitmap = new SkiaSharp.SKBitmap(4, 4);
                bitmap.Erase(SkiaSharp.SKColors.Blue);
                using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                using var file = new ByteArrayContent(png.ToArray());
                file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                using var form = new MultipartFormDataContent { { file, "file", "avatar.png" } };
                Assert.Equal(HttpStatusCode.OK, (await avatarClient.PutAsync("/api/v1/me/avatar", form)).StatusCode);
            }
            using (var restartedHost = new PostgresHost(connection))
            using (var restartedClient = restartedHost.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }))
            {
                restartedClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", oldAccess);
                var stored = await restartedClient.GetByteArrayAsync("/api/v1/me/avatar");
                await using var avatarConnection = new NpgsqlConnection(connection);
                await avatarConnection.OpenAsync();
                await using var query = new NpgsqlCommand("SELECT avatar_data FROM ai_orchestration.app_user WHERE email = 'integration@example.test'", avatarConnection);
                Assert.Equal(stored, (byte[])(await query.ExecuteScalarAsync())!);
                Assert.Equal(HttpStatusCode.NoContent, (await restartedClient.DeleteAsync("/api/v1/me/avatar")).StatusCode);
                Assert.Equal(DBNull.Value, await query.ExecuteScalarAsync());
            }

            var rotated = await client.PostAsync("/api/v1/auth/refresh", null);
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
            var rotatedCookie = rotated.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            Assert.NotEqual(oldCookie, rotatedCookie);
            Assert.False(string.IsNullOrWhiteSpace(
                (await rotated.Content.ReadFromJsonAsync<LoginResponse>())?.AccessToken));
            using var replayClient = host.CreateClient(new WebApplicationFactoryClientOptions
            {
                HandleCookies = false, BaseAddress = new Uri("https://localhost")
            });
            using var replay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
            replay.Headers.TryAddWithoutValidation("Cookie", oldCookie);
            Assert.Equal(HttpStatusCode.Conflict, (await replayClient.SendAsync(replay)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);

            var logoutLogin = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "integration@example.test", password = "long-passphrase-123" });
            Assert.Equal(HttpStatusCode.OK, logoutLogin.StatusCode);
            var logoutCookie = logoutLogin.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
            logoutRequest.Headers.TryAddWithoutValidation("Cookie", logoutCookie);
            var logout = await client.SendAsync(logoutRequest);
            Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
            var logoutBody = await logout.Content.ReadFromJsonAsync<LogoutResponse>();
            Assert.Equal("Logged out successfully.", logoutBody?.Message);
            Assert.Contains(logout.Headers.GetValues("Set-Cookie"),
                value => value.StartsWith("orchestra_refresh=", StringComparison.OrdinalIgnoreCase));
            using var loggedOutRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
            loggedOutRefresh.Headers.TryAddWithoutValidation("Cookie", logoutCookie);
            Assert.Equal(HttpStatusCode.Unauthorized, (await replayClient.SendAsync(loggedOutRefresh)).StatusCode);

            // Competing refresh requests must never leave two usable successors.
            using var concurrencyHost = new PostgresHost(connection);
            using var concurrencyClient = concurrencyHost.CreateClient(new WebApplicationFactoryClientOptions
                { HandleCookies = false, BaseAddress = new Uri("https://localhost") });
            var concurrentLogin = await concurrencyClient.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "integration@example.test", password = "long-passphrase-123" });
            var concurrentCookie = concurrentLogin.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            using var firstRotation = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
            using var secondRotation = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
            firstRotation.Headers.TryAddWithoutValidation("Cookie", concurrentCookie);
            secondRotation.Headers.TryAddWithoutValidation("Cookie", concurrentCookie);
            var rotations = await Task.WhenAll(concurrencyClient.SendAsync(firstRotation), concurrencyClient.SendAsync(secondRotation));
            Assert.Single(rotations, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(rotations, r => r.StatusCode == HttpStatusCode.Conflict);
            var winner = rotations.Single(r => r.StatusCode == HttpStatusCode.OK);
            using var successor = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
            successor.Headers.TryAddWithoutValidation("Cookie", winner.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
            Assert.Equal(HttpStatusCode.Unauthorized, (await concurrencyClient.SendAsync(successor)).StatusCode);

            // Logout-all must also revoke a token created by a competing refresh.
            var deviceA = await concurrencyClient.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "integration@example.test", password = "long-passphrase-123" });
            var deviceB = await concurrencyClient.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "integration@example.test", password = "long-passphrase-123" });
            deviceA.EnsureSuccessStatusCode();
            deviceB.EnsureSuccessStatusCode();
            var accessA = (await deviceA.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
            var accessB = (await deviceB.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
            var cookieA = deviceA.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            var cookieB = deviceB.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            using var logoutAll = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout-all");
            logoutAll.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessA);
            using var competingRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
            competingRefresh.Headers.TryAddWithoutValidation("Cookie", cookieB);
            var results = await Task.WhenAll(concurrencyClient.SendAsync(logoutAll), concurrencyClient.SendAsync(competingRefresh));
            Assert.Equal(HttpStatusCode.OK, results[0].StatusCode);
            Assert.Contains(results[1].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized });
            var cookies = new List<string> { cookieA, cookieB };
            if (results[1].IsSuccessStatusCode)
                cookies.Add(results[1].Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
            foreach (var cookie in cookies)
            {
                using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
                refresh.Headers.TryAddWithoutValidation("Cookie", cookie);
                // The rotated historical token is a replay; all live tokens are unauthorized.
                var result = await concurrencyClient.SendAsync(refresh);
                Assert.Contains(result.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Conflict });
            }
            foreach (var access in new[] { accessA, accessB })
            {
                using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
                me.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
                Assert.Equal(HttpStatusCode.Unauthorized, (await concurrencyClient.SendAsync(me)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/forgot-password",
                new { email = "integration@example.test" })).StatusCode);
            var resetCode = host.Email.Sent.Last().Code;
            var resetResponse = await client.PostAsJsonAsync("/api/v1/auth/verify-reset-code",
                new { email = "integration@example.test", code = resetCode });
            Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
            var resetToken = (await resetResponse.Content.ReadFromJsonAsync<ResetTokenResponse>())!.ResetToken;
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/reset-password",
                new { resetToken, newPassword = "different-passphrase-456" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/reset-password",
                new { resetToken, newPassword = "different-passphrase-456" })).StatusCode);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", oldAccess);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
        });

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var source = Environment.GetEnvironmentVariable("ORCHESTRA_TEST_ADMIN_CONNECTION") ??
            new ConfigurationBuilder().AddUserSecrets(typeof(Program).Assembly, optional: true)
                .Build().GetConnectionString("OrchestraDb");
        Assert.False(string.IsNullOrWhiteSpace(source),
            "Set ORCHESTRA_TEST_ADMIN_CONNECTION or the API's OrchestraDb User Secret.");
        var databaseName = "orchestra_auth_test_" + Guid.NewGuid().ToString("N")[..12];
        var testBuilder = new NpgsqlConnectionStringBuilder(source) { Database = databaseName, Pooling = false };
        await using var admin = new NpgsqlConnection(source);
        await admin.OpenAsync();
        var created = false;
        try
        {
            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
                await create.ExecuteNonQueryAsync();
            created = true;
            await using (var database = new NpgsqlConnection(testBuilder.ConnectionString))
            {
                await database.OpenAsync();
                var baseline = FindSchemaFile();
                var migrations = Path.GetDirectoryName(baseline)!;
                foreach (var file in new[] { baseline, Path.Combine(migrations, "002_user_avatar.sql"),
                    Path.Combine(migrations, "003_api_stored_procedures.sql"),
                    Path.Combine(migrations, "004_model_creation_procedure.sql"),
                    Path.Combine(migrations, "004_platform_setting_procedures.sql"),
                    Path.Combine(migrations, "004_provider_read_procedures.sql"),
                    Path.Combine(migrations, "004_super_admin_feature_flags.sql"),
                    Path.Combine(migrations, "005_platform_setting_apply_flag.sql"),
                    Path.Combine(migrations, "005_provide_health_&_Maintenanace.sql"),
                    Path.Combine(migrations, "006_provider_diagnostic_adapters.sql") })
                {
                    await using var apply = new NpgsqlCommand(await File.ReadAllTextAsync(file), database);
                    await apply.ExecuteNonQueryAsync();
                }
            }
            await test(testBuilder.ConnectionString);
        }
        finally
        {
            if (created)
            {
                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }

    private static string FindSchemaFile()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path is not null && !File.Exists(Path.Combine(path.FullName, "Orchestra.sln")))
            path = path.Parent;
        if (path is null) throw new FileNotFoundException("Orchestra.sln was not found.");
        return Path.Combine(path.FullName, "infrastructure", "database", "migrations", "001_initial_schema.sql");
    }

    private sealed record ResetTokenResponse(string ResetToken);
    private sealed record PlatformSettingDto(
        string Key, JsonElement? Value, string? Description, bool IsSensitive, bool IsApplied);

    private sealed class DiagnosticsHandler : HttpMessageHandler
    {
        internal List<JsonElement> Requests { get; } = [];
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.NoContent;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://ai-service.example.test/internal/v1/catalog/test", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer test-service-token", request.Headers.Authorization?.ToString());
            Requests.Add(await request.Content!.ReadFromJsonAsync<JsonElement>(ct));
            return new HttpResponseMessage(Status)
            {
                Content = Status == HttpStatusCode.NoContent ? null : new StringContent("private upstream details")
            };
        }
    }

    private sealed class PostgresHost(string connection) : WebApplicationFactory<Program>
    {
        public FakeEmailSender Email { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:SigningKey"] = new string('s', 48),
                ["Auth:CodeHashKey"] = new string('c', 48),
                ["DevelopmentEmail:AllowAnyDomain"] = "true",
                ["DevelopmentEmail:Recipient"] = "tester@example.test",
                ["ConnectionStrings:OrchestraDb"] = connection,
                ["Bootstrap:Admin:Email"] = "admin@example.test",
                ["Bootstrap:Admin:Password"] = "initial-admin-passphrase",
                ["Bootstrap:SuperAdmin:Email"] = "super@example.test",
                ["Bootstrap:SuperAdmin:Password"] = "initial-super-passphrase"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Email);
            });
        }
    }
}
