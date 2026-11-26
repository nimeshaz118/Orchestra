using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class ModelReadEndpointTests
{
    [Fact]
    public async Task Super_admin_can_list_models_get_one_model_and_get_not_found_for_unknown_id()
    {
        await using var host = new ApiHost();
        var firstModelId = await host.SeedModelAsync("ACTIVE");
        var secondModelId = await host.SeedModelAsync("INACTIVE");
        await host.SeedUserAsync("model-read-admin@example.test", "SUPER_ADMIN");
        using var client = await host.LoginClientAsync("model-read-admin@example.test");

        var list = await client.GetFromJsonAsync<List<ModelResponse>>(
            "/api/v1/super-admin/models");
        Assert.NotNull(list);
        Assert.Contains(list, model => model.Id == firstModelId);
        Assert.Contains(list, model => model.Id == secondModelId);

        var get = await client.GetAsync($"/api/v1/super-admin/models/{firstModelId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var model = await get.Content.ReadFromJsonAsync<ModelResponse>();
        Assert.NotNull(model);
        Assert.Equal(firstModelId, model.Id);
        Assert.Equal("Test model", model.DisplayName);
        Assert.Equal("ACTIVE", model.Status);
        Assert.Equal(JsonValueKind.Object, model.Capabilities.ValueKind);

        var missing = await client.GetAsync(
            $"/api/v1/super-admin/models/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private sealed record ModelResponse(
        Guid Id, string DisplayName, string Status, JsonElement Capabilities);
}
