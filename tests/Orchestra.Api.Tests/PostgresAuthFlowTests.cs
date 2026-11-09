using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orchestra.Api.Modules.Auth;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class PostgresAuthFlowTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Verification_refresh_reuse_and_password_reset_are_single_use()
    {
        if (Environment.GetEnvironmentVariable("ORCHESTRA_RUN_POSTGRES_TESTS") != "true") return;
        var source = Environment.GetEnvironmentVariable("ORCHESTRA_TEST_ADMIN_CONNECTION") ??
            new ConfigurationBuilder().AddUserSecrets(typeof(Program).Assembly, optional: true)
                .Build().GetConnectionString("OrchestraDb");
        Assert.False(string.IsNullOrWhiteSpace(source),
            "Set ORCHESTRA_TEST_ADMIN_CONNECTION or the API's OrchestraDb User Secret.");
        var databaseName = "orchestra_auth_test_" + Guid.NewGuid().ToString("N")[..12];
        var adminBuilder = new NpgsqlConnectionStringBuilder(source);
        var testBuilder = new NpgsqlConnectionStringBuilder(source) { Database = databaseName, Pooling = false };
        await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        var created = false;
        try
        {
            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
                await create.ExecuteNonQueryAsync();
            created = true;
            var script = await File.ReadAllTextAsync(FindSchemaFile());
            await using (var database = new NpgsqlConnection(testBuilder.ConnectionString))
            {
                await database.OpenAsync();
                await using var apply = new NpgsqlCommand(script, database);
                await apply.ExecuteNonQueryAsync();
            }

            using var host = new PostgresHost(testBuilder.ConnectionString);
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
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/change-password",
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
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/change-password",
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

            await using (var database = new NpgsqlConnection(testBuilder.ConnectionString))
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

            var verifyCode = Assert.Single(host.Email.Sent).Code;
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/verify-email",
                new { email = "integration@example.test", code = verifyCode })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/auth/verify-email",
                new { email = "integration@example.test", code = verifyCode })).StatusCode);

            var login = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "integration@example.test", password = "long-passphrase-123" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var oldCookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            var oldAccess = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
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
            Assert.Equal(HttpStatusCode.Unauthorized, (await replayClient.SendAsync(replay)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);

            var logoutLogin = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "integration@example.test", password = "long-passphrase-123" });
            Assert.Equal(HttpStatusCode.OK, logoutLogin.StatusCode);
            var logoutCookie = logoutLogin.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
            logoutRequest.Headers.TryAddWithoutValidation("Cookie", logoutCookie);
            var logout = await client.SendAsync(logoutRequest);
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            Assert.Contains(logout.Headers.GetValues("Set-Cookie"),
                value => value.StartsWith("orchestra_refresh=", StringComparison.OrdinalIgnoreCase));
            using var loggedOutRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
            loggedOutRefresh.Headers.TryAddWithoutValidation("Cookie", logoutCookie);
            Assert.Equal(HttpStatusCode.Unauthorized, (await replayClient.SendAsync(loggedOutRefresh)).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/forgot-password",
                new { email = "integration@example.test" })).StatusCode);
            var resetCode = host.Email.Sent.Last().Code;
            var resetResponse = await client.PostAsJsonAsync("/api/v1/auth/verify-reset-code",
                new { email = "integration@example.test", code = resetCode });
            Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
            var resetToken = (await resetResponse.Content.ReadFromJsonAsync<ResetTokenResponse>())!.ResetToken;
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/reset-password",
                new { resetToken, newPassword = "different-passphrase-456" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/reset-password",
                new { resetToken, newPassword = "different-passphrase-456" })).StatusCode);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", oldAccess);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
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
