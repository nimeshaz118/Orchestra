using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;
using Orchestra.Api.Modules.Providers;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class ProviderHealthEndpointsTests
{
    private static readonly string[] DegradedProviderErrors = ["AI_SERVICE_UNAVAILABLE", "CATALOG_TEST_FAILED"];

    [Fact]
    public async Task Health_uses_recent_conclusive_checks_for_the_requested_provider()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("super@example.test", "SUPER_ADMIN");
        using var client = await host.LoginClientAsync("super@example.test");
        var now = DateTime.UtcNow;
        var provider = MakeProvider("Provider A", now);
        var other = MakeProvider("Provider B", now);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
            db.AiProviders.AddRange(provider, other);
            db.SystemLogs.AddRange(
                MakeCheck(provider, now.AddHours(-25), true, 2000, null),
                MakeCheck(provider, now.AddHours(-4), true, 100, null),
                MakeCheck(provider, now.AddHours(-3), false, null, "CATALOG_TEST_FAILED"),
                MakeCheck(provider, now.AddHours(-2), true, 300, null),
                MakeCheck(provider, now.AddHours(-1), null, null, "AI_SERVICE_UNAVAILABLE"),
                MakeCheck(other, now.AddHours(-25), true, 50, null),
                MakeCheck(other, now.AddHours(-1), null, null, "INVALID_TEST_CONFIGURATION"));
            await db.SaveChangesAsync();
        }

        var health = (await client.GetFromJsonAsync<ProviderHealthDto>(
            $"/api/v1/super-admin/providers/{provider.ProviderId}/health"))!;
        Assert.Equal("degraded", health.CurrentStatus);
        Assert.Equal(66.67, health.AvailabilityPercent24h);
        Assert.Equal(200.0, health.AverageLatencyMs24h);
        Assert.Equal(now.AddHours(-2), health.LastSuccessfulCheckAt);
        Assert.Equal(now.AddHours(-1), health.LastCheckedAt);
        Assert.Equal(DegradedProviderErrors, health.RecentSafeErrorCategories);

        var stale = (await client.GetFromJsonAsync<ProviderHealthDto>(
            $"/api/v1/super-admin/providers/{other.ProviderId}/health"))!;
        Assert.Equal("unknown", stale.CurrentStatus);
        Assert.Null(stale.AvailabilityPercent24h);
        Assert.Null(stale.AverageLatencyMs24h);
        Assert.Equal(now.AddHours(-25), stale.LastSuccessfulCheckAt);
        Assert.Equal("INVALID_TEST_CONFIGURATION", Assert.Single(stale.RecentSafeErrorCategories));
    }

    private static AiProvider MakeProvider(string name, DateTime now) => new()
    {
        ProviderId = Guid.NewGuid(),
        ProviderName = name,
        BaseUrl = "https://provider.example.test/v1",
        Configuration = "{}",
        Status = "INACTIVE",
        TimeoutSeconds = 30,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static SystemLog MakeCheck(
        AiProvider provider, DateTime checkedAt, bool? succeeded, long? latency, string? error) => new()
    {
        SystemLogId = Guid.NewGuid(),
        EventCode = "PROVIDER_HEALTH_CHECK",
        Category = "ai_diagnostics",
        Severity = "INFO",
        SourceService = "orchestra-api",
        Message = "Provider diagnostic completed.",
        CorrelationId = Guid.NewGuid(),
        ErrorCode = error,
        Metadata = JsonSerializer.Serialize(new { providerId = provider.ProviderId, providerSucceeded = succeeded, latencyMs = latency }),
        OccurredAt = checkedAt
    };
}
