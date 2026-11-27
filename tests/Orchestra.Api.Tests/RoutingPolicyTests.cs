using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class RoutingPolicyTests
{
    [Fact]
    public async Task Super_admin_can_list_all_policies_and_policies_by_department()
    {
        await using var host = new ApiHost();
        var modelId = await host.SeedModelAsync("ACTIVE");
        var departmentId = await host.SeedDepartmentAsync("Engineering", "ACTIVE");
        var client = await SuperAdminClientAsync(host);

        await client.PostAsJsonAsync("/api/v1/super-admin/routing-policies",
            Request(modelId, "GENERAL_PURPOSE"));
        await client.PostAsJsonAsync("/api/v1/super-admin/routing-policies",
            Request(modelId, "CODING", "DEPARTMENT", departmentId));

        var all = await client.GetFromJsonAsync<List<RoutingPolicyResponse>>(
            "/api/v1/super-admin/routing-policies");
        Assert.Equal(2, all?.Count);

        var department = await client.GetFromJsonAsync<List<RoutingPolicyResponse>>(
            $"/api/v1/super-admin/routing-policies/department/{departmentId}");
        Assert.Equal("CODING", Assert.Single(department!).TaskCategory);
    }

    [Fact]
    public async Task Super_admin_can_get_a_routing_policy_and_missing_policy_returns_not_found()
    {
        await using var host = new ApiHost();
        var modelId = await host.SeedModelAsync("ACTIVE");
        var client = await SuperAdminClientAsync(host);
        var created = await client.PostAsJsonAsync("/api/v1/super-admin/routing-policies",
            Request(modelId, "CODING"));
        var policy = await created.Content.ReadFromJsonAsync<RoutingPolicyResponse>();

        var response = await client.GetAsync($"/api/v1/super-admin/routing-policies/{policy!.PolicyId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await response.Content.ReadFromJsonAsync<RoutingPolicyResponse>();
        Assert.Equal(policy.PolicyId, fetched?.PolicyId);
        Assert.Equal("CODING", fetched?.TaskCategory);

        var missing = await client.GetAsync($"/api/v1/super-admin/routing-policies/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("ROUTING_POLICY_NOT_FOUND",
            (await missing.Content.ReadFromJsonAsync<ApiErrorResponse>())?.Code);
    }

    [Fact]
    public async Task Super_admin_can_create_policies_for_both_task_categories_and_audit_is_written()
    {
        await using var host = new ApiHost();
        var modelId = await host.SeedModelAsync("ACTIVE");
        var client = await SuperAdminClientAsync(host);

        var general = await client.PostAsJsonAsync("/api/v1/super-admin/routing-policies",
            Request(modelId, "GENERAL_PURPOSE"));
        var coding = await client.PostAsJsonAsync("/api/v1/super-admin/routing-policies",
            Request(modelId, "CODING"));

        Assert.Equal(HttpStatusCode.Created, general.StatusCode);
        Assert.Equal(HttpStatusCode.Created, coding.StatusCode);
        var generalPolicy = await general.Content.ReadFromJsonAsync<RoutingPolicyResponse>();
        var codingPolicy = await coding.Content.ReadFromJsonAsync<RoutingPolicyResponse>();
        Assert.Equal("GENERAL_PURPOSE", generalPolicy?.TaskCategory);
        Assert.Equal("CODING", codingPolicy?.TaskCategory);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        Assert.Equal(2, await db.RoutingPolicies.CountAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action == "ROUTING_POLICY_CREATED"));
    }

    [Fact]
    public async Task Non_super_admins_are_forbidden()
    {
        await using var host = new ApiHost();
        var modelId = await host.SeedModelAsync("ACTIVE");
        await host.SeedUserAsync("user@example.test", "USER", await host.SeedDepartmentAsync("Engineering", "ACTIVE"));
        await host.SeedUserAsync("admin@example.test", "ADMIN");
        var user = await host.LoginClientAsync("user@example.test");
        var admin = await host.LoginClientAsync("admin@example.test");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await user.PostAsJsonAsync("/api/v1/super-admin/routing-policies", Request(modelId, "CODING"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsJsonAsync("/api/v1/super-admin/routing-policies", Request(modelId, "CODING"))).StatusCode);
    }

    [Fact]
    public async Task Validation_rejects_invalid_scope_category_priority_and_references()
    {
        await using var host = new ApiHost();
        var modelId = await host.SeedModelAsync("ACTIVE");
        var client = await SuperAdminClientAsync(host);

        var invalid = await client.PostAsJsonAsync("/api/v1/super-admin/routing-policies",
            new
            {
                policyName = " ",
                modelId,
                taskCategory = "OTHER",
                scopeType = "GLOBAL",
                departmentId = Guid.NewGuid(),
                priority = 0,
                isEnabled = true
            });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var error = await invalid.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("INVALID_ROUTING_POLICY", error?.Code);
        Assert.Contains("policyName", error!.Errors!.Keys);
        Assert.Contains("taskCategory", error.Errors.Keys);
        Assert.Contains("departmentId", error.Errors.Keys);
        Assert.Contains("priority", error.Errors.Keys);

        var disabledModel = await host.SeedModelAsync("INACTIVE");
        var disabled = await client.PostAsJsonAsync("/api/v1/super-admin/routing-policies",
            Request(disabledModel, "CODING"));
        Assert.Equal((HttpStatusCode)422, disabled.StatusCode);

        var missingModel = await client.PostAsJsonAsync("/api/v1/super-admin/routing-policies",
            Request(Guid.NewGuid(), "CODING"));
        Assert.Equal(HttpStatusCode.NotFound, missingModel.StatusCode);
    }

    [Fact]
    public async Task Department_scope_requires_an_existing_department()
    {
        await using var host = new ApiHost();
        var modelId = await host.SeedModelAsync("ACTIVE");
        var client = await SuperAdminClientAsync(host);

        var missingDepartment = await client.PostAsJsonAsync("/api/v1/super-admin/routing-policies",
            new
            {
                policyName = "Department policy",
                modelId,
                taskCategory = "CODING",
                scopeType = "DEPARTMENT",
                departmentId = (Guid?)null,
                priority = 1,
                isEnabled = true
            });
        Assert.Equal(HttpStatusCode.BadRequest, missingDepartment.StatusCode);

        var unknownDepartment = await client.PostAsJsonAsync("/api/v1/super-admin/routing-policies",
            Request(modelId, "CODING", "DEPARTMENT", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, unknownDepartment.StatusCode);
    }

    private static object Request(
        Guid modelId,
        string category,
        string scope = "GLOBAL",
        Guid? departmentId = null) =>
        new
        {
            policyName = $"{category} policy",
            description = "Routing policy",
            modelId,
            taskCategory = category,
            scopeType = scope,
            departmentId,
            priority = 1,
            isEnabled = true
        };

    private static async Task<HttpClient> SuperAdminClientAsync(ApiHost host)
    {
        await host.SeedUserAsync("super@example.test", "SUPER_ADMIN");
        return await host.LoginClientAsync("super@example.test");
    }

    private sealed record RoutingPolicyResponse(
        Guid PolicyId,
        string TaskCategory,
        string ScopeType);

    private sealed record ApiErrorResponse(
        string Code,
        string Message,
        string RequestId,
        Dictionary<string, string[]>? Errors);
}

public static class RoutingPolicyTestData
{
    public static async Task<Guid> SeedModelAsync(this ApiHost host, string status)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var provider = new AiProvider
        {
            ProviderId = Guid.NewGuid(),
            ProviderName = $"provider-{Guid.NewGuid():N}",
            BaseUrl = "https://provider.test",
            Status = "ACTIVE",
            TimeoutSeconds = 60,
            Configuration = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var model = new AiModel
        {
            ModelId = Guid.NewGuid(),
            ProviderId = provider.ProviderId,
            ProviderModelKey = $"model-{Guid.NewGuid():N}",
            DisplayName = "Test model",
            ModelType = "CHAT",
            Status = status,
            QualityTier = 3,
            Capabilities = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Provider = provider
        };
        db.AiProviders.Add(provider);
        db.AiModels.Add(model);
        await db.SaveChangesAsync();
        return model.ModelId;
    }
}
