using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class DepartmentManagementTests
{
    [Fact]
    public async Task Super_admin_can_get_and_partially_update_a_department()
    {
        await using var host = new ApiHost();
        var id = await host.SeedDepartmentAsync("Engineering", "ACTIVE");
        var client = await SuperAdminClientAsync(host);

        var get = await client.GetAsync($"/api/v1/super-admin/departments/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var before = await get.Content.ReadFromJsonAsync<DepartmentResponse>();
        Assert.Equal("Engineering", before?.Name);

        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/super-admin/departments/{id}",
            new { name = "Platform", description = "Platform engineering" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var after = await patch.Content.ReadFromJsonAsync<DepartmentResponse>();
        Assert.Equal("Platform", after?.Name);
        Assert.Equal("Platform engineering", after?.Description);

        // Omitted name is preserved; explicit null clears description.
        patch = await client.PatchAsJsonAsync(
            $"/api/v1/super-admin/departments/{id}", new { description = (string?)null });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        after = await patch.Content.ReadFromJsonAsync<DepartmentResponse>();
        Assert.Equal("Platform", after?.Name);
        Assert.Null(after?.Description);
    }

    [Fact]
    public async Task Missing_department_returns_not_found()
    {
        await using var host = new ApiHost();
        var client = await SuperAdminClientAsync(host);
        var id = Guid.NewGuid();

        var get = await client.GetAsync($"/api/v1/super-admin/departments/{id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        var getError = await get.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("DEPARTMENT_NOT_FOUND", getError?.Code);
        Assert.False(string.IsNullOrWhiteSpace(getError?.RequestId));

        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/super-admin/departments/{id}", new { name = "Platform" });
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
        var patchError = await patch.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("DEPARTMENT_NOT_FOUND", patchError?.Code);

        var activate = await client.PostAsync(
            $"/api/v1/super-admin/departments/{id}/activate", null);
        Assert.Equal(HttpStatusCode.NotFound, activate.StatusCode);
        Assert.Equal("DEPARTMENT_NOT_FOUND",
            (await activate.Content.ReadFromJsonAsync<ApiError>())?.Code);

        var deactivate = await client.PostAsync(
            $"/api/v1/super-admin/departments/{id}/deactivate", null);
        Assert.Equal(HttpStatusCode.NotFound, deactivate.StatusCode);
        Assert.Equal("DEPARTMENT_NOT_FOUND",
            (await deactivate.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    [Fact]
    public async Task Patch_rejects_empty_invalid_denied_and_duplicate_changes()
    {
        await using var host = new ApiHost();
        var id = await host.SeedDepartmentAsync("Engineering", "ACTIVE");
        await host.SeedDepartmentAsync("Finance", "ACTIVE");
        var client = await SuperAdminClientAsync(host);
        var path = $"/api/v1/super-admin/departments/{id}";

        var empty = await client.PatchAsJsonAsync(path, new { });
        Assert.Equal((HttpStatusCode)422, empty.StatusCode);
        var emptyError = await empty.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("INVALID_DEPARTMENT_UPDATE", emptyError?.Code);
        Assert.Contains("request", emptyError!.Errors!.Keys);

        var invalidName = await client.PatchAsJsonAsync(path, new { name = " " });
        Assert.Equal((HttpStatusCode)422, invalidName.StatusCode);
        var nameError = await invalidName.Content.ReadFromJsonAsync<ApiError>();
        Assert.Contains("name", nameError!.Errors!.Keys);

        var invalidDescription = await client.PatchAsJsonAsync(
            path, new { description = new string('x', 2001) });
        Assert.Equal((HttpStatusCode)422, invalidDescription.StatusCode);
        var descriptionError = await invalidDescription.Content.ReadFromJsonAsync<ApiError>();
        Assert.Contains("description", descriptionError!.Errors!.Keys);

        var denied = await client.PatchAsJsonAsync(path, new { status = "ARCHIVED" });
        Assert.Equal((HttpStatusCode)422, denied.StatusCode);
        var deniedError = await denied.Content.ReadFromJsonAsync<ApiError>();
        Assert.Contains("status", deniedError!.Errors!.Keys);

        var duplicate = await client.PatchAsJsonAsync(path, new { name = "Finance" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var duplicateError = await duplicate.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("DEPARTMENT_EXISTS", duplicateError?.Code);
        Assert.Contains("name", duplicateError!.Errors!.Keys);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        Assert.Equal("Engineering",
            (await db.Departments.SingleAsync(d => d.DepartmentId == id)).DepartmentName);
    }

    [Fact]
    public async Task Admin_cannot_use_super_admin_department_detail_routes()
    {
        await using var host = new ApiHost();
        var id = await host.SeedDepartmentAsync("Engineering", "ACTIVE");
        await host.SeedUserAsync("admin@example.test", "ADMIN");
        var client = await host.LoginClientAsync("admin@example.test");
        var path = $"/api/v1/super-admin/departments/{id}";

        var get = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal("FORBIDDEN", (await get.Content.ReadFromJsonAsync<ApiError>())?.Code);

        var patch = await client.PatchAsJsonAsync(path, new { name = "Platform" });
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
        Assert.Equal("FORBIDDEN", (await patch.Content.ReadFromJsonAsync<ApiError>())?.Code);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsync($"{path}/activate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsync($"{path}/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_department_request_returns_common_error_envelope()
    {
        await using var host = new ApiHost();
        var client = host.CreateClient();

        var response = await client.GetAsync($"/api/v1/super-admin/departments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("INVALID_SESSION", error?.Code);
        Assert.False(string.IsNullOrWhiteSpace(error?.Message));
        Assert.False(string.IsNullOrWhiteSpace(error?.RequestId));
        Assert.False(error?.Retryable);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync(
                $"/api/v1/super-admin/departments/{Guid.NewGuid()}/activate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync(
                $"/api/v1/super-admin/departments/{Guid.NewGuid()}/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task Super_admin_can_repeat_status_actions_and_status_controls_signup()
    {
        await using var host = new ApiHost();
        var id = await host.SeedDepartmentAsync("Engineering", "ACTIVE");
        var client = await SuperAdminClientAsync(host);
        var basePath = $"/api/v1/super-admin/departments/{id}";

        var deactivate = await client.PostAsync($"{basePath}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.Equal("INACTIVE",
            (await deactivate.Content.ReadFromJsonAsync<DepartmentResponse>())?.Status);

        var repeatDeactivate = await client.PostAsync($"{basePath}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, repeatDeactivate.StatusCode);
        Assert.Equal("INACTIVE",
            (await repeatDeactivate.Content.ReadFromJsonAsync<DepartmentResponse>())?.Status);

        var publicDepartments = await host.CreateClient()
            .GetFromJsonAsync<List<DepartmentResponse>>("/api/v1/public/departments");
        Assert.DoesNotContain(publicDepartments!, department => department.Id == id);

        var registration = new
        {
            email = "department.user@example.test",
            password = "long-passphrase-123",
            firstName = "Department",
            lastName = "User",
            departmentId = id
        };
        var rejectedRegistration = await host.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/register", registration);
        Assert.Equal((HttpStatusCode)422, rejectedRegistration.StatusCode);
        Assert.Equal("DEPARTMENT_NOT_AVAILABLE",
            (await rejectedRegistration.Content.ReadFromJsonAsync<ApiError>())?.Code);

        var activate = await client.PostAsync($"{basePath}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        Assert.Equal("ACTIVE",
            (await activate.Content.ReadFromJsonAsync<DepartmentResponse>())?.Status);

        var repeatActivate = await client.PostAsync($"{basePath}/activate", null);
        Assert.Equal(HttpStatusCode.OK, repeatActivate.StatusCode);
        Assert.Equal("ACTIVE",
            (await repeatActivate.Content.ReadFromJsonAsync<DepartmentResponse>())?.Status);

        publicDepartments = await host.CreateClient()
            .GetFromJsonAsync<List<DepartmentResponse>>("/api/v1/public/departments");
        Assert.Contains(publicDepartments!, department => department.Id == id);

        var acceptedRegistration = await host.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/register", registration);
        Assert.Equal(HttpStatusCode.Created, acceptedRegistration.StatusCode);
    }

    private static async Task<HttpClient> SuperAdminClientAsync(ApiHost host)
    {
        await host.SeedUserAsync("super@example.test", "SUPER_ADMIN");
        return await host.LoginClientAsync("super@example.test");
    }

    private sealed record DepartmentResponse(Guid Id, string Name, string? Description, string Status);
}