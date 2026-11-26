using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Orchestra.Api.Common;

namespace Orchestra.Api.Modules.Maintenance;

[ApiController]
[Authorize(Roles = "SUPER_ADMIN")]
[MaintenanceRecovery]
[Route("api/v1/super-admin/maintenance")]
public sealed class MaintenanceController(MaintenanceService maintenance) : ControllerBase
{
    [HttpPost("start")]
    [SuppressMessage("SonarAnalyzer.CSharp", "S6932",
        Justification = "The endpoint checks the number of header values and preserves commas within a single key.")]
    public async Task<ActionResult<MaintenanceStarted>> Start(
        StartMaintenanceRequest request, CancellationToken ct)
    {
        var key = Request.Headers["Idempotency-Key"];
        if (key.Count > 1)
            throw new ApiException(400, "VALIDATION_ERROR", "Supply only one Idempotency-Key.");
        return Ok(await maintenance.StartAsync(request, key.Count == 0 ? null : key[0], ActorId(), CorrelationId(), ct));
    }

    [HttpPost("end")]
    public async Task<ActionResult<MaintenanceEnded>> End(EndMaintenanceRequest request, CancellationToken ct) =>
        Ok(await maintenance.EndAsync(request, ActorId(), CorrelationId(), ct));

    private Guid ActorId() => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    private static Guid CorrelationId() =>
        Guid.TryParse(Activity.Current?.TraceId.ToHexString(), out var id) ? id : Guid.NewGuid();
}

[ApiController]
[AllowAnonymous]
[Route("api/v1/system/status")]
[EnableRateLimiting("systemStatus")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SystemStatusController(MaintenanceService maintenance) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SystemStatus>> Get(CancellationToken ct) =>
        Ok(await maintenance.GetSystemStatusAsync(ct));
}
