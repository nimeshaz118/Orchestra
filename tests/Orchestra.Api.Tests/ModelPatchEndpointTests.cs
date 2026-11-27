using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Data;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class ModelPatchEndpointTests
{
    [Fact]
    public async Task Super_admin_can_patch_model_fields_and_the_change_is_audited()
    {
        await using var host = new ApiHost();
        var modelId = await host.SeedModelAsync("ACTIVE");
        await host.SeedUserAsync("model-patch-admin@example.test", "SUPER_ADMIN");
        using var client = await host.LoginClientAsync("model-patch-admin@example.test");

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/super-admin/models/{modelId}",
            new
            {
                displayName = "  Updated model  ",
                qualityTier = 4,
                capabilities = new { streaming = true }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var modelResponse = await response.Content.ReadFromJsonAsync<ModelResponse>();
        Assert.NotNull(modelResponse);
        Assert.Equal("Updated model", modelResponse.DisplayName);
        Assert.Equal("ACTIVE", modelResponse.Status);
        Assert.Equal((short)4, modelResponse.QualityTier);
        Assert.True(modelResponse.Capabilities.GetProperty("streaming").GetBoolean());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var model = await db.AiModels.SingleAsync(m => m.ModelId == modelId);
        var actor = await db.AppUsers.SingleAsync(u => u.Email == "model-patch-admin@example.test");
        Assert.Equal(actor.UserId, model.UpdatedByUserId);
        Assert.Equal("MODEL_UPDATED",
            await db.AuditLogs.Where(a => a.TargetId == modelId).Select(a => a.Action).SingleAsync());
    }

    [Fact]
    public async Task Model_patch_rejects_empty_invalid_and_noneditable_fields()
    {
        await using var host = new ApiHost();
        var modelId = await host.SeedModelAsync("ACTIVE");
        await host.SeedUserAsync("model-patch-admin@example.test", "SUPER_ADMIN");
        using var client = await host.LoginClientAsync("model-patch-admin@example.test");

        var empty = await client.PatchAsJsonAsync(
            $"/api/v1/super-admin/models/{modelId}", new { });
        Assert.Equal((HttpStatusCode)422, empty.StatusCode);

        var invalid = await client.PatchAsJsonAsync(
            $"/api/v1/super-admin/models/{modelId}", new { displayName = " " });
        Assert.Equal((HttpStatusCode)422, invalid.StatusCode);

        var nonEditable = await client.PatchAsJsonAsync(
            $"/api/v1/super-admin/models/{modelId}", new { status = "INACTIVE" });
        Assert.Equal((HttpStatusCode)422, nonEditable.StatusCode);

        var missing = await client.PatchAsJsonAsync(
            $"/api/v1/super-admin/models/{Guid.NewGuid()}", new { displayName = "Updated" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private sealed record ModelResponse(
        Guid Id, string DisplayName, string Status, short QualityTier, JsonElement Capabilities);
}
