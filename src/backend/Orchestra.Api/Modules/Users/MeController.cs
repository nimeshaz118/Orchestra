using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Orchestra.Api.Common;
using Orchestra.Api.Modules.Auth;
using Orchestra.Api.Modules.Users.Interfaces;

namespace Orchestra.Api.Modules.Users;

[ApiController, Authorize]
[Route("api/v1/me")]
public sealed class MeController(AuthService auth, ISessionService session) : ControllerBase
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


    [HttpDelete("sessions/{sessionId:guid}"), EnableRateLimiting("sessionRevoke")]
    public async Task<IActionResult> RevokeSession(
        Guid sessionId,
        CancellationToken ct)
    {
        var userId = User.GetUserId();

        await session.RevokeSessionAsync(
            userId,
            sessionId,
            ct);

        return Ok(new
        {
            message = "Session revoked successfully."
        });
    }

    // Revoke all other sessions
    [HttpDelete("sessions/others"), EnableRateLimiting("sessionRevoke")]
    public async Task<IActionResult> RevokeOtherSessions(
        CancellationToken ct)
    {
        var userId = User.GetUserId();

        // Obtain the current refresh token from your existing
        // refresh-token cookie/header mechanism.
        var currentToken = Request.Cookies["orchestra_refresh"];

        var count = await session.RevokeOtherSessionsAsync(
            userId,
            currentToken,
            ct);

        return Ok(new
        {
            message = "Other sessions revoked successfully.",
            revokedCount = count
        });
    }

}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
        => Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
