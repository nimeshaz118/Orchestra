
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;
using Orchestra.Api.Modules.Domains;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class DomainsEndpointsTests
{
    private const string Url = "/api/v1/admin/domains";

    // TEST 1: Unauthenticated users cannot view domains.
    [Fact]
    public async Task Get_domains_without_login_returns_401()
    {
        await using var host = new ApiHost();
        var client = host.CreateClient();

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    // TEST 2: Normal USER cannot access Admin Domain APIs.
    [Fact]
    public async Task Normal_user_cannot_access_domain_apis()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("user@example.test", "USER");

        var client = await host.LoginClientAsync("user@example.test");

        var get = await client.GetAsync(Url);
        var post = await client.PostAsJsonAsync(
            Url, new { domainName = "school.edu" });

        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    // TEST 3: Both ADMIN and SUPER_ADMIN can list domains.
    [Theory]
    [InlineData("ADMIN")]
    [InlineData("SUPER_ADMIN")]
    public async Task Admin_roles_can_get_domains(string role)
    {
        await using var host = new ApiHost();
        var email = $"{role.ToLowerInvariant()}@example.test";

        await host.SeedUserAsync(email, role);
        var client = await host.LoginClientAsync(email);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var domains = await response.Content
            .ReadFromJsonAsync<List<DomainDto>>();

        Assert.NotNull(domains);
        Assert.Empty(domains);
    }

    // TEST 4: GET returns existing domains in alphabetical order.
    // Current service returns both active and inactive records.
    [Fact]
    public async Task Get_domains_returns_existing_records()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("admin@example.test", "ADMIN");

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<OrchestraDbContext>();

            var now = DateTime.UtcNow;

            db.ApprovedEmailDomains.AddRange(
                new ApprovedEmailDomain
                {
                    DomainId = Guid.NewGuid(),
                    DomainName = "zeta.edu",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new ApprovedEmailDomain
                {
                    DomainId = Guid.NewGuid(),
                    DomainName = "alpha.edu",
                    IsActive = false,
                    CreatedAt = now,
                    UpdatedAt = now
                });

            await db.SaveChangesAsync();
        }

        var client = await host.LoginClientAsync("admin@example.test");
        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var domains = await response.Content
            .ReadFromJsonAsync<List<DomainDto>>();

        Assert.NotNull(domains);
        Assert.Equal(2, domains.Count);
        Assert.Equal("alpha.edu", domains[0].DomainName);
        Assert.Equal("zeta.edu", domains[1].DomainName);
        Assert.False(domains[0].IsActive);
        Assert.True(domains[1].IsActive);
    }

    // TEST 5: Both privileged roles can create valid domains.
    [Theory]
    [InlineData("ADMIN")]
    [InlineData("SUPER_ADMIN")]
    public async Task Admin_roles_can_create_valid_domain(string role)
    {
        await using var host = new ApiHost();
        var email = $"{role.ToLowerInvariant()}@example.test";

        await host.SeedUserAsync(email, role);
        var client = await host.LoginClientAsync(email);

        var response = await client.PostAsJsonAsync(
            Url, new { domainName = "  UNIVERSITY.EDU  " });

        Assert.Equal(HttpStatusCode.Created,
            response.StatusCode);

        var domain = await response.Content
            .ReadFromJsonAsync<DomainDto>();

        Assert.NotNull(domain);
        Assert.Equal("university.edu", domain.DomainName);
        Assert.True(domain.IsActive);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<OrchestraDbContext>();

        Assert.Equal(1, await db.ApprovedEmailDomains
            .CountAsync(d => d.DomainName == "university.edu"));
    }

    // TEST 6: Invalid domain inputs return 422.
    [Theory]
    [InlineData(null, "DOMAIN_REQUIRED")]
    [InlineData("", "DOMAIN_REQUIRED")]
    [InlineData("   ", "DOMAIN_REQUIRED")]
    [InlineData("invalid-domain", "INVALID_DOMAIN")]
    [InlineData("@university.edu", "INVALID_DOMAIN")]
    [InlineData("https://university.edu", "INVALID_DOMAIN")]
    [InlineData("*.university.edu", "INVALID_DOMAIN")]
    [InlineData("my domain.edu", "INVALID_DOMAIN")]
    public async Task Create_invalid_domain_returns_422(
        string? domainName,
        string expectedCode)
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("admin@example.test", "ADMIN");
        var client = await host.LoginClientAsync("admin@example.test");

        var response = await client.PostAsJsonAsync(
            Url, new { domainName });

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            response.StatusCode);

        var error = await response.Content
            .ReadFromJsonAsync<DomainErrorResponse>();

        Assert.Equal(expectedCode, error?.Code);
    }

    // TEST 7: Creating an existing active domain returns 409.
    [Fact]
    public async Task Duplicate_domain_returns_409()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("admin@example.test", "ADMIN");
        var client = await host.LoginClientAsync("admin@example.test");

        var first = await client.PostAsJsonAsync(
            Url, new { domainName = "university.edu" });

        var second = await client.PostAsJsonAsync(
            Url, new { domainName = "UNIVERSITY.EDU" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var error = await second.Content
            .ReadFromJsonAsync<DomainErrorResponse>();

        Assert.Equal("DOMAIN_ALREADY_APPROVED", error?.Code);
    }

    // TEST 8: An inactive domain is reactivated rather
    // than inserted again.
    [Fact]
    public async Task Creating_inactive_domain_reactivates_existing_row()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("admin@example.test", "ADMIN");

        var domainId = Guid.NewGuid();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<OrchestraDbContext>();

            db.ApprovedEmailDomains.Add(new ApprovedEmailDomain
            {
                DomainId = domainId,
                DomainName = "old.edu",
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var client = await host.LoginClientAsync("admin@example.test");

        var response = await client.PostAsJsonAsync(
            Url, new { domainName = "old.edu" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var domain = await response.Content
            .ReadFromJsonAsync<DomainDto>();

        Assert.NotNull(domain);
        Assert.Equal(domainId, domain.DomainId);
        Assert.True(domain.IsActive);

        using var checkScope = host.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider
            .GetRequiredService<OrchestraDbContext>();

        Assert.Equal(1, await checkDb.ApprovedEmailDomains
            .CountAsync(d => d.DomainName == "old.edu"));
    }

    private sealed record DomainErrorResponse(
        string Code,
        string Message);
}
