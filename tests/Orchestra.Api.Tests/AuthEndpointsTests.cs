using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;
using Orchestra.Api.Modules.Auth;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class AuthEndpointsTests
{
    [Fact]
    public async Task Public_departments_show_only_active_entries()
    {
        await using var host = new ApiHost();
        var active = await host.SeedDepartmentAsync("Engineering", "ACTIVE");
        await host.SeedDepartmentAsync("Archived", "ARCHIVED");
        var response = await host.CreateClient().GetAsync("/api/v1/public/departments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var departments = await response.Content.ReadFromJsonAsync<List<DepartmentDtoForTest>>();
        Assert.Single(departments!);
        Assert.Equal(active, departments![0].Id);
    }

    [Fact]
    public async Task Registration_creates_pending_user_and_hashed_code()
    {
        await using var host = new ApiHost();
        var department = await host.SeedDepartmentAsync("Engineering", "ACTIVE");
        var response = await host.CreateClient().PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "new.user@example.test", password = "long-passphrase-123",
            firstName = "New", lastName = "User", departmentId = department
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var user = await db.AppUsers.SingleAsync();
        Assert.Equal("USER", user.RoleCode);
        Assert.Equal("PENDING", user.Status);
        Assert.NotEqual("long-passphrase-123", user.PasswordHash);
        var challenge = await db.VerificationCodes.SingleAsync();
        Assert.Equal("EMAIL_VERIFICATION", challenge.Purpose);
        Assert.Equal(64, challenge.CodeHash.Length);
        Assert.Equal("new.user@example.test", Assert.Single(host.Email.Sent).AccountEmail);
    }

    [Fact]
    public async Task Login_profile_and_sessions_belong_to_authenticated_user()
    {
        await using var host = new ApiHost();
        var department = await host.SeedDepartmentAsync("Engineering", "ACTIVE");
        await host.SeedUserAsync("person@example.test", "USER", department);
        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "person@example.test", password = "long-passphrase-123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrEmpty(body?.AccessToken));
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1", cookie, StringComparison.OrdinalIgnoreCase);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        var session = await client.GetAsync("/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Equal("person@example.test", (await session.Content.ReadFromJsonAsync<UserDto>())?.Email);
        var profile = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal("person@example.test", (await profile.Content.ReadFromJsonAsync<UserDto>())?.Email);
        var sessions = await client.GetAsync("/api/v1/me/sessions");
        Assert.Equal(HttpStatusCode.OK, sessions.StatusCode);
        Assert.Single((await sessions.Content.ReadFromJsonAsync<List<SessionDto>>())!);
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Session_requires_a_valid_access_token_and_login_rejects_bad_credentials()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("person@example.test", "USER");
        var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/auth/session")).StatusCode);
        var failedLogin = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "person@example.test", password = "incorrect-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, failedLogin.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS",
            (await failedLogin.Content.ReadFromJsonAsync<ApiErrorForTest>())?.Code);
        Assert.False(failedLogin.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Logout_without_credentials_returns_401()
    {
        await using var host = new ApiHost();
        var response = await host.CreateClient().PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_invalidates_access_token_for_subsequent_api_calls()
    {
        await using var host = new ApiHost();
        var department = await host.SeedDepartmentAsync("Engineering", "ACTIVE");
        await host.SeedUserAsync("logout.test@example.test", "USER", department);
        var client = host.CreateClient();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "logout.test@example.test", password = "long-passphrase-123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await login.Content.ReadFromJsonAsync<LoginResponse>();
        var accessToken = loginBody!.AccessToken;

        // Verify protected call works before logout
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var profileBefore = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, profileBefore.StatusCode);

        // Perform logout
        var logoutResponse = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
        var logoutBody = await logoutResponse.Content.ReadFromJsonAsync<LogoutResponse>();
        Assert.Equal("Logged out successfully.", logoutBody?.Message);

        // Verify all other API calls using the authentication token now fail
        var profileAfter = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, profileAfter.StatusCode);

        var sessionAfter = await client.GetAsync("/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, sessionAfter.StatusCode);
    }

    [Fact]
    public async Task Logout_with_bearer_token_only_invalidates_token_and_returns_message()
    {
        await using var host = new ApiHost();
        var department = await host.SeedDepartmentAsync("Engineering", "ACTIVE");
        await host.SeedUserAsync("bearer.only@example.test", "USER", department);
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "bearer.only@example.test", password = "long-passphrase-123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var accessToken = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var profileBefore = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, profileBefore.StatusCode);

        // Logout with Bearer token only (no cookie sent)
        var logoutResponse = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
        var logoutBody = await logoutResponse.Content.ReadFromJsonAsync<LogoutResponse>();
        Assert.Equal("Logged out successfully.", logoutBody?.Message);

        var profileAfter = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, profileAfter.StatusCode);
    }

    [Fact]
    public async Task Logout_with_cookie_only_invalidates_associated_access_token()
    {
        await using var host = new ApiHost();
        var department = await host.SeedDepartmentAsync("Engineering", "ACTIVE");
        await host.SeedUserAsync("cookie.only@example.test", "USER", department);
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "cookie.only@example.test", password = "long-passphrase-123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        var accessToken = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;

        // Verify token works
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var profileBefore = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, profileBefore.StatusCode);

        // Call logout sending cookie only (no Authorization header)
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutRequest.Headers.TryAddWithoutValidation("Cookie", cookie);
        var logoutResponse = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
        var logoutBody = await logoutResponse.Content.ReadFromJsonAsync<LogoutResponse>();
        Assert.Equal("Logged out successfully.", logoutBody?.Message);

        // Access token belonging to that session should now be unauthorized
        var profileAfter = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, profileAfter.StatusCode);
    }

    [Fact]
    public async Task Refresh_rejects_disabled_account_and_clears_refresh_cookie()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("disabled@example.test", "USER");
        var refreshToken = TokenService.NewOpaqueToken();
        await host.SeedDisabledUserRefreshTokenAsync("disabled@example.test", refreshToken);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.TryAddWithoutValidation("Cookie", $"orchestra_refresh={refreshToken}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("INVALID_SESSION", (await response.Content.ReadFromJsonAsync<ApiErrorForTest>())?.Code);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("orchestra_refresh=", StringComparison.OrdinalIgnoreCase) &&
                value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Super_admin_can_use_admin_departments_but_admin_cannot_use_super_admin_route()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("admin@example.test", "ADMIN");
        await host.SeedUserAsync("super@example.test", "SUPER_ADMIN");
        var admin = await host.LoginClientAsync("admin@example.test");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/admin/departments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/super-admin/departments")).StatusCode);
        var super = await host.LoginClientAsync("super@example.test");
        Assert.Equal(HttpStatusCode.OK, (await super.GetAsync("/api/v1/admin/departments")).StatusCode);
        var created = await super.PostAsJsonAsync("/api/v1/super-admin/departments",
            new { name = "Finance", description = "Finance team" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await super.PostAsJsonAsync("/api/v1/admin/departments",
            new { name = "Finance", description = "Duplicate" })).StatusCode);
    }

    [Fact]
    public async Task Forgot_password_returns_not_found_for_unknown_email()
    {
        await using var host = new ApiHost();
        var response = await host.CreateClient().PostAsJsonAsync("/api/v1/auth/forgot-password",
            new { email = "unknown@example.test" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(host.Email.Sent);
    }

    [Theory]
    [InlineData("bad-email", "password")]
    [InlineData("person@example.test", "")]
    [InlineData("", "password")]
    public async Task Login_invalid_input_returns_400(string email, string password)
    {
        await using var host = new ApiHost();
        var response = await host.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("INACTIVE", true)]
    [InlineData("PENDING", false)]
    public async Task Correct_credentials_for_unavailable_account_return_403(string status, bool verified)
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("person@example.test", "USER");
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
            var user = await db.AppUsers.SingleAsync();
            user.Status = status;
            user.EmailVerifiedAt = verified ? DateTime.UtcNow : null;
            await db.SaveChangesAsync();
        }
        var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "person@example.test", password = "wrong-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "person@example.test", password = "long-passphrase-123" })).StatusCode);
    }

    [Fact]
    public async Task Failed_logins_throttle_account()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("person@example.test", "USER");
        var client = host.CreateClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "person@example.test", password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "person@example.test", password = "long-passphrase-123" })).StatusCode);
    }

    [Fact]
    public async Task Logout_preserves_other_device_and_session_rejects_disabled_account()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("person@example.test", "USER");
        var first = await host.LoginClientAsync("person@example.test");
        var second = await host.LoginClientAsync("person@example.test");
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
            Assert.Equal(2, await db.RefreshTokens.CountAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/v1/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/v1/auth/session")).StatusCode);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
            var user = await db.AppUsers.SingleAsync();
            user.Status = "INACTIVE";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await second.GetAsync("/api/v1/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Unknown_logout_cookie_returns_404_and_forged_bearer_returns_401()
    {
        await using var host = new ApiHost();
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", "orchestra_refresh=unknown");
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "forged.token.value");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
    }

    [Fact]
    public async Task Logout_rejects_cookie_from_another_session_without_revoking_either()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync("person@example.test", "USER");
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var first = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "person@example.test", password = "long-passphrase-123" });
        var second = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "person@example.test", password = "long-passphrase-123" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await first.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        client.DefaultRequestHeaders.Add("Cookie", second.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        Assert.Equal(2, await db.RefreshTokens.CountAsync(t => t.RevokedAt == null));
    }

    private sealed record DepartmentDtoForTest(Guid Id, string Name);
    private sealed record ApiErrorForTest(string Code, string Message, string RequestId);
}

public sealed class ApiHost : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    public FakeEmailSender Email { get; } = new();
    public bool FailSessionSave { get; set; }
    public bool FailAvatarSave { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => { logging.ClearProviders(); logging.AddConsole(); });
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:SigningKey"] = new string('s', 48),
            ["Auth:CodeHashKey"] = new string('c', 48),
            ["DevelopmentEmail:AllowAnyDomain"] = "true",
            ["DevelopmentEmail:Recipient"] = "tester@example.test",
            ["ConnectionStrings:OrchestraDb"] = "Host=localhost;Database=unused",
            ["Bootstrap:Admin:Email"] = "",
            ["Bootstrap:Admin:Password"] = "",
            ["Bootstrap:SuperAdmin:Email"] = "",
            ["Bootstrap:SuperAdmin:Password"] = ""
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<OrchestraDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<OrchestraDbContext>>();
            services.AddDbContext<OrchestraDbContext>(o => o.UseInMemoryDatabase(_dbName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(new SessionSaveFailure(this)));
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
        });
    }

    private sealed class SessionSaveFailure(ApiHost host) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (host.FailSessionSave || (host.FailAvatarSave &&
                eventData.Context!.ChangeTracker.Entries<AppUser>().Any(e => e.Property(u => u.AvatarData).IsModified))) throw new DbUpdateException("Simulated database failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    public async Task<Guid> SeedDepartmentAsync(string name, string status)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var department = new Department
        {
            DepartmentId = Guid.NewGuid(), DepartmentName = name, Status = status,
            Configuration = "{}", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return department.DepartmentId;
    }

    public async Task SeedUserAsync(string email, string role, Guid? department = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        if (!await db.Roles.AnyAsync(r => r.RoleCode == role))
            db.Roles.Add(new Role { RoleCode = role, DisplayName = role, Permissions = "{}",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        var user = new AppUser
        {
            UserId = Guid.NewGuid(), Email = email, RoleCode = role, DepartmentId = department,
            FirstName = "Test", LastName = "User", Status = "ACTIVE",
            EmailVerifiedAt = DateTime.UtcNow, SecurityStamp = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, "long-passphrase-123");
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
    }

    public async Task SeedDisabledUserRefreshTokenAsync(string email, string plainToken)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var user = await db.AppUsers.SingleAsync(u => u.Email == email);
        user.Status = "INACTIVE";
        db.RefreshTokens.Add(new RefreshToken
        {
            RefreshTokenId = Guid.NewGuid(), UserId = user.UserId,
            TokenHash = TokenService.HashOpaqueToken(plainToken), FamilyId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();
    }

    public async Task<HttpClient> LoginClientAsync(string email)
    {
        var client = CreateClient();
        var result = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = "long-passphrase-123" });
        result.EnsureSuccessStatusCode();
        var response = await result.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response!.AccessToken);
        return client;
    }
}

public sealed class FakeEmailSender : IEmailSender
{
    public List<(string AccountEmail, string Purpose, string Code)> Sent { get; } = [];
    public Task SendCodeAsync(string accountEmail, string purpose, string code, CancellationToken ct)
    {
        Sent.Add((accountEmail, purpose, code));
        return Task.CompletedTask;
    }
}
