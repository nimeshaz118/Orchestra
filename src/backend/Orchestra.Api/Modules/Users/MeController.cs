using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orchestra.Api.Common;
using Orchestra.Api.Modules.Auth;

namespace Orchestra.Api.Modules.Users;

[ApiController, Authorize]
[Route("api/v1/me")]
public sealed class MeController(AuthService auth) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var id = User.GetUserId();
        var user = await auth.CurrentUserAsync(id, ct)
            ?? throw new ApiException(401, "INVALID_SESSION", "Please sign in again.");
        return Ok(new UserDto(user.UserId, user.Email, user.FirstName, user.LastName,
            user.RoleCode, user.DepartmentId, user.Status, user.MustChangePassword));
    }
    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions(CancellationToken ct)
    {
        var id = User.GetUserId();
        var refreshToken = Request.Cookies["orchestra_refresh"];
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ApiException(401, "INVALID_SESSION", "The session has expired.");

        return Ok(await auth.SessionsAsync(id, refreshToken, ct));
    }

}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
        => Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
