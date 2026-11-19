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
    public async Task<ActionResult<List<EffectivePlatformSettingDto>>> Get(CancellationToken ct)
        => Ok(await settings.GetEffectiveAsync(ct));

    [HttpPost]
    public async Task<ActionResult<EffectivePlatformSettingDto>> Create(
        CreatePlatformSettingRequest request, CancellationToken ct)
    {
        var actor = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var result = await settings.CreateAsync(request, actor, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPatch]
    public async Task<ActionResult<EffectivePlatformSettingDto>> Update(
        UpdatePlatformSettingRequest request, CancellationToken ct)
    {
        var actor = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await settings.UpdateAsync(request, actor, ct));
    }
}
