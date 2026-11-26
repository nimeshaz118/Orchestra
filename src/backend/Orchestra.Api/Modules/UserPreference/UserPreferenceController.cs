using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Orchestra.Api.Controllers.UserPreference;

[ApiController, Authorize]
[Route("api/v1/me/preferences")]
public sealed class UserPreferenceController(UserPreferenceService preferences) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyDictionary<string, JsonElement>>> Get(CancellationToken ct)
    {
        var userId = CurrentUserId();
        return Ok(await preferences.GetAsync(userId, ct));
    }

    [HttpPatch]
    public async Task<ActionResult<IReadOnlyDictionary<string, JsonElement>>> Update(
        Dictionary<string, JsonElement> request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        return Ok(await preferences.UpdateAsync(userId, request, ct));
    }

    private Guid CurrentUserId()
        => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
