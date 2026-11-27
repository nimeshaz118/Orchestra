using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Orchestra.Api.Modules.FeatureFlags;

[ApiController]
[Authorize(Roles = "SUPER_ADMIN")]
[Route("api/v1/super-admin/feature-flags")]
public sealed class FeatureFlagsController(FeatureFlagsService featureFlags) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<FeatureFlagDto>>> List(CancellationToken ct)
        => Ok(await featureFlags.ListAsync(ct));
}
