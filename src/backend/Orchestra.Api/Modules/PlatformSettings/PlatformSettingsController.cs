using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Orchestra.Api.Modules.PlatformSettings;

[ApiController]
[Authorize(Roles = "SUPER_ADMIN")]
[Route("api/v1/super-admin/platform-settings")]
public sealed class PlatformSettingsController(PlatformSettingsService settings) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PlatformSettingDto>>> Get(CancellationToken ct)
        => Ok(await settings.GetAllAsync(ct));

    [HttpPost("apply")]
    public async Task<ActionResult<PlatformSettingDto>> SetApplied(
        ApplyPlatformSettingRequest request, CancellationToken ct)
    {
        var actor = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await settings.SetAppliedAsync(request, actor, ct));
    }

    [HttpPost]
    public async Task<ActionResult<PlatformSettingDto>> Create(
        CreatePlatformSettingRequest request, CancellationToken ct)
    {
        var actor = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var setting = await settings.CreateAsync(request, actor, ct);
        return CreatedAtAction(nameof(Get), null, setting);
    }

    [HttpPatch]
    public async Task<ActionResult<PlatformSettingDto>> Update(
        UpdatePlatformSettingRequest request, CancellationToken ct)
    {
        var actor = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await settings.UpdateAsync(request, actor, ct));
    }
}
