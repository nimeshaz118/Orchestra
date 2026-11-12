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
            var script = await File.ReadAllTextAsync(FindSchemaFile()) + "\n" +
                await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(FindSchemaFile())!, "002_user_avatar.sql"));
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
           

            // Confirm the initial registration email was generated.
            Assert.Single(host.Email.Sent);

            // Move the initial code's creation time back by 61 seconds.
            // This simulates waiting for the resend cooldown.
            await using (var connection =
                new NpgsqlConnection(testBuilder.ConnectionString))
            {
                await connection.OpenAsync();

                await using var command = new NpgsqlCommand("""
                    UPDATE ai_orchestration.verification_code
                    SET created_at = created_at - INTERVAL '61 seconds'
                    WHERE user_id = (
                        SELECT user_id
                        FROM ai_orchestration.app_user
                        WHERE email = 'integration@example.test'
                    )
                    AND purpose = 'EMAIL_VERIFICATION'
                    """, connection);

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
            await using (var upgradeConnection = new NpgsqlConnection(testBuilder.ConnectionString))
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
            using (var avatarHost = new PostgresHost(testBuilder.ConnectionString))
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
            using (var restartedHost = new PostgresHost(testBuilder.ConnectionString))
            using (var restartedClient = restartedHost.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }))
            {
                restartedClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", oldAccess);
                var stored = await restartedClient.GetByteArrayAsync("/api/v1/me/avatar");
                await using var connection = new NpgsqlConnection(testBuilder.ConnectionString);
                await connection.OpenAsync();
                await using var query = new NpgsqlCommand("SELECT avatar_data FROM ai_orchestration.app_user WHERE email = 'integration@example.test'", connection);
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
            using var concurrencyHost = new PostgresHost(testBuilder.ConnectionString);
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
