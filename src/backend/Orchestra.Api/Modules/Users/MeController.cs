using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Orchestra.Api.Common;
using Orchestra.Api.Modules.Auth;

namespace Orchestra.Api.Modules.Users;

[ApiController, Authorize]
[Route("api/v1/me")]
public sealed class MeController(AuthService auth, MeService me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(ToUserDto(await auth.CurrentUserAsync(id, ct)));
    }

    [HttpPatch]
    public async Task<IActionResult> Update(UpdateMeRequest request, CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(ToUserDto(await me.UpdateAsync(id, request, ct)));
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions(CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await auth.SessionsAsync(id, Request.Cookies["orchestra_refresh"], ct));
    }

    [HttpPost("password"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        await auth.ChangePasswordAsync(id, request.CurrentPassword, request.NewPassword, ct);
        Response.Cookies.Delete("orchestra_refresh", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/api/v1"
        });
        return Ok(new { message = "Password changed successfully." });
    }

    private static MeDto ToUserDto(Models.Entities.AppUser? user)
    {
        if (user is null)
            throw new ApiException(401, "INVALID_SESSION", "Please sign in again.");
        return new MeDto(user.UserId, user.Email, user.FirstName, user.LastName,
            user.DisplayName, user.PhoneNumber, user.JobTitle, user.RoleCode, user.DepartmentId,
            user.Status, user.MustChangePassword, user.CreatedAt, user.UpdatedAt);
    }
}
