using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Data;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class ModelStatusTests
{
    [Fact]
    public async Task Super_admin_can_enable_and_disable_a_model_and_each_change_is_audited()
    {
        await using var host = new ApiHost();
        var modelId = await host.SeedModelAsync("INACTIVE");
        await host.SeedUserAsync("model-admin@example.test", "SUPER_ADMIN");
        using var client = await host.LoginClientAsync("model-admin@example.test");

        var enable = await client.PostAsync(
            $"/api/v1/super-admin/models/{modelId}/enable", content: null);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        Assert.Equal("ACTIVE", (await enable.Content.ReadFromJsonAsync<ModelResponse>())?.Status);

        var enableAgain = await client.PostAsync(
            $"/api/v1/super-admin/models/{modelId}/enable", content: null);
        Assert.Equal(HttpStatusCode.OK, enableAgain.StatusCode);

        var disable = await client.PostAsync(
            $"/api/v1/super-admin/models/{modelId}/disable", content: null);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.Equal("INACTIVE", (await disable.Content.ReadFromJsonAsync<ModelResponse>())?.Status);

        var disableAgain = await client.PostAsync(
            $"/api/v1/super-admin/models/{modelId}/disable", content: null);
        Assert.Equal(HttpStatusCode.OK, disableAgain.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        Assert.Equal("INACTIVE", (await db.AiModels.SingleAsync(m => m.ModelId == modelId)).Status);
        var actions = await db.AuditLogs.Select(a => a.Action).ToArrayAsync();
        Assert.Equal(2, actions.Length);
        Assert.Contains("MODEL_ENABLED", actions);
        Assert.Contains("MODEL_DISABLED", actions);
    }

    [Fact]
    public async Task Enabling_or_disabling_a_missing_model_returns_not_found()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("model-admin@example.test", "SUPER_ADMIN");
        using var client = await host.LoginClientAsync("model-admin@example.test");
        var missingModelId = Guid.NewGuid();

        var enable = await client.PostAsync(
            $"/api/v1/super-admin/models/{missingModelId}/enable", content: null);
        Assert.Equal(HttpStatusCode.NotFound, enable.StatusCode);

        var disable = await client.PostAsync(
            $"/api/v1/super-admin/models/{missingModelId}/disable", content: null);
        Assert.Equal(HttpStatusCode.NotFound, disable.StatusCode);
    }

    private sealed record ModelResponse(Guid Id, string Status);
}
