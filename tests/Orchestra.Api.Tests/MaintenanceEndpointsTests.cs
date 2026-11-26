using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Modules.Maintenance;
using Orchestra.Api.Modules.Providers;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class MaintenanceEndpointsTests
{
    private static readonly string[] RestoredServices = ["authenticated_api", "ai"];
    private static readonly string[] MaintenanceAuditActions = ["MAINTENANCE_STARTED", "MAINTENANCE_ENDED"];

    private const string Start = "/api/v1/super-admin/maintenance/start";
    private const string End = "/api/v1/super-admin/maintenance/end";
    private const string Status = "/api/v1/system/status";
    private const string Super = "super@example.test";
    private const string Message = "Scheduled service update";

    [Theory]
    [InlineData(Start)]
    [InlineData(End)]
    public async Task Requires_super_admin_and_rejects_invalid_input(string path)
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync(Super, "SUPER_ADMIN");
        await host.SeedUserAsync("admin@example.test", "ADMIN");
        using var anonymous = host.CreateClient();
        using var admin = await host.LoginClientAsync("admin@example.test");
        using var super = await host.LoginClientAsync(Super);

        using var noToken = await anonymous.PostAsJsonAsync(path, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        using var wrongRole = await admin.PostAsJsonAsync(path, new { });
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
        using var invalid = await super.PostAsJsonAsync(path, new { });
        await AssertErrorAsync(invalid, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Maintenance_publishes_status_and_waits_for_readiness_before_restoring_work()
    {
        using var diagnostics = new MaintenanceProbe();
        await using var host = new ApiHost { AiDiagnosticsHandler = diagnostics };
        await host.SeedUserAsync(Super, "SUPER_ADMIN");
        await host.SeedUserAsync("person@example.test", "USER");
        using var super = await host.LoginClientAsync(Super);
        using var person = await host.LoginClientAsync("person@example.test");
        using var anonymous = host.CreateClient();

        var data = MakeStartRequest("authenticated_operations");
        var started = await StartAsync(super, data, "scheduled-update");
        Assert.True(started.Active);
        Assert.Equal("authenticated_operations", started.Scope);
        Assert.Equal(data.ExpectedEndAt!.Value.UtcDateTime, started.ExpectedEndAt);
        Assert.Equal(started, await StartAsync(super, data, "scheduled-update"));

        using var conflict = await super.PostAsJsonAsync(Start, data);
        await AssertErrorAsync(conflict, HttpStatusCode.Conflict, "MAINTENANCE_ALREADY_ACTIVE");
        using var changed = StartMessage(data with { Reason = "A different update" }, "scheduled-update");
        using var idempotencyConflict = await super.SendAsync(changed);
        await AssertErrorAsync(idempotencyConflict, HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");

        using var blocked = await person.GetAsync("/api/v1/me");
        await AssertErrorAsync(blocked, HttpStatusCode.ServiceUnavailable, "MAINTENANCE_ACTIVE");
        using var noToken = await anonymous.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        using var statusResponse = await anonymous.GetAsync(Status);
        var rawStatus = await statusResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(data.Reason!, rawStatus);
        var status = (await statusResponse.Content.ReadFromJsonAsync<SystemStatus>())!;
        Assert.Equal("maintenance", status.OverallStatus);
        Assert.True(status.MaintenanceActive);
        Assert.Equal(Message, status.PublicMessage);

        var outcome = new { summary = "The update succeeded." };
        using var notReady = await super.PostAsJsonAsync(End, outcome);
        await AssertErrorAsync(notReady, HttpStatusCode.Conflict, "REQUIRED_SERVICE_NOT_READY");
        Assert.True((await anonymous.GetFromJsonAsync<SystemStatus>(Status))!.MaintenanceActive);

        diagnostics.Ready = true;
        // Obtain a fresh recovery token during maintenance to prove sign-in remains available.
        using var recovery = await host.LoginClientAsync(Super);
        using var end = await recovery.PostAsJsonAsync(End, outcome);
        end.EnsureSuccessStatusCode();
        var ended = (await end.Content.ReadFromJsonAsync<MaintenanceEnded>())!;
        Assert.Equal(started.MaintenanceId, ended.MaintenanceId);
        Assert.False(ended.Active);
        Assert.Equal(RestoredServices, ended.RestoredServices);
        Assert.Empty(ended.UnavailableServices);
        Assert.False((await anonymous.GetFromJsonAsync<SystemStatus>(Status))!.MaintenanceActive);
        using var profile = await person.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var repeatedEnd = await recovery.PostAsJsonAsync(End, outcome);
        await AssertErrorAsync(repeatedEnd, HttpStatusCode.Conflict, "MAINTENANCE_NOT_ACTIVE");

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
        var audit = await db.AuditLogs.OrderBy(item => item.OccurredAt).ToListAsync();
        Assert.Equal(MaintenanceAuditActions, audit.Select(item => item.Action));
        Assert.Equal(outcome.summary, audit[1].Reason);
        Assert.All(audit, item => Assert.Equal(started.MaintenanceId, item.TargetId));
    }

    [Theory]
    [InlineData("allow_finish", HttpStatusCode.OK)]
    [InlineData("cancel", HttpStatusCode.ServiceUnavailable)]
    public async Task Ai_only_scope_applies_the_in_flight_policy(string policy, HttpStatusCode expected)
    {
        using var diagnostics = new MaintenanceProbe();
        await using var host = new ApiHost { AiDiagnosticsHandler = diagnostics };
        await host.SeedUserAsync(Super, "SUPER_ADMIN");
        using var super = await host.LoginClientAsync(Super);
        using var created = await super.PostAsJsonAsync("/api/v1/super-admin/providers", new
        {
            providerName = "Test Provider", baseUrl = "https://provider.example.test/v1",
            credentialReference = "TEST_PROVIDER_KEY", timeoutSeconds = 15
        });
        created.EnsureSuccessStatusCode();
        var provider = (await created.Content.ReadFromJsonAsync<ProviderDto>())!;
        var path = $"/api/v1/super-admin/providers/{provider.ProviderId}/test";

        var inFlight = super.PostAsync(path, null);
        try
        {
            await diagnostics.ProbeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var started = await StartAsync(super, MakeStartRequest("ai_only") with { ActiveRequestPolicy = policy });
            Assert.Equal(1, started.AffectedActiveRequestCount);

            using var blocked = await super.PostAsync(path, null);
            await AssertErrorAsync(blocked, HttpStatusCode.ServiceUnavailable, "MAINTENANCE_ACTIVE");
            using var profile = await super.GetAsync("/api/v1/me");
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);

            if (policy == "allow_finish")
                diagnostics.ReleaseProbe.TrySetResult();
            using var completed = await inFlight.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(expected, completed.StatusCode);
            if (policy == "cancel")
                await AssertErrorAsync(completed, expected, "MAINTENANCE_CANCELLED");
        }
        finally
        {
            diagnostics.ReleaseProbe.TrySetResult();
        }

        diagnostics.Ready = true;
        using var end = await super.PostAsJsonAsync(End, new { summary = "The AI service is ready." });
        end.EnsureSuccessStatusCode();
        using var resumed = await super.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
    }

    private static StartMaintenanceRequest MakeStartRequest(string scope) =>
        new("Roll out the service update", Message, DateTimeOffset.Parse("2028-02-01T15:00:00.123456+05:30"),
            scope, "allow_finish");

    private static async Task<MaintenanceStarted> StartAsync(
        HttpClient client, StartMaintenanceRequest data, string? key = null)
    {
        using var request = StartMessage(data, key);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MaintenanceStarted>())!;
    }

    private static HttpRequestMessage StartMessage(StartMaintenanceRequest data, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Start) { Content = JsonContent.Create(data) };
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    private sealed class MaintenanceProbe : HttpMessageHandler
    {
        public bool Ready { get; set; }
        public TaskCompletionSource ProbeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseProbe { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("Bearer test-service-token", request.Headers.Authorization?.ToString());
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/internal/v1/health/ready", request.RequestUri?.AbsolutePath);
                return new HttpResponseMessage(Ready ? HttpStatusCode.NoContent : HttpStatusCode.ServiceUnavailable);
            }

            Assert.Equal("/internal/v1/catalog/test", request.RequestUri?.AbsolutePath);
            ProbeEntered.TrySetResult();
            await ReleaseProbe.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
