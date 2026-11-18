using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Orchestra.Api.Modules.Providers;

[ApiController]
[Route("api/v1/super-admin/providers")]
[Authorize(Roles = "SUPER_ADMIN")]
public sealed class ProvidersController(ProvidersService providers) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await providers.ListAsync(ct));

    [HttpGet("{providerId:guid}")]
    public async Task<IActionResult> Get(Guid providerId, CancellationToken ct) =>
        Ok(await providers.GetAsync(providerId, ct));

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateProviderRequest request, CancellationToken ct)
    {
        var result = await providers.CreateAsync(request, ActorId(), ct);
        return CreatedAtAction(nameof(Get),
            new { providerId = result.ProviderId }, result);
    }

    [HttpPatch("{providerId:guid}")]
    public async Task<IActionResult> Update(
        Guid providerId, UpdateProviderRequest request, CancellationToken ct) =>
        Ok(await providers.UpdateAsync(providerId, request, ActorId(), ct));

    [HttpPost("{providerId:guid}/enable")]
    public async Task<IActionResult> Enable(
        Guid providerId, CancellationToken ct) =>
        Ok(await providers.EnableAsync(providerId, ActorId(), ct));

    [HttpPost("{providerId:guid}/disable")]
    public async Task<IActionResult> Disable(
        Guid providerId, CancellationToken ct) =>
        Ok(await providers.DisableAsync(providerId, ActorId(), ct));

    private Guid ActorId() =>
        Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}