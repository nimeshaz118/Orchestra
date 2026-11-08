using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Orchestra.Api.Common;

namespace Orchestra.Api.Modules.Auth;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthService auth, IConfiguration configuration) : ControllerBase
{
    private const string CookieName = "orchestra_refresh";

    [HttpPost("register"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        await auth.RegisterAsync(request, ct);
        return StatusCode(201, new { message = "Check your email for a verification code." });
    }

    [HttpPost("verify-email"), EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyEmail(EmailCodeRequest request, CancellationToken ct)
    {
        await auth.VerifyEmailAsync(request, ct);
        return NoContent();
    }

    [HttpPost("resend-verification"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ResendVerification(EmailRequest request, CancellationToken ct)
    {
        await auth.ResendVerificationAsync(request, ct);
        return Ok(new { message = "If the account needs verification, a code will be sent." });
    }

    [HttpPost("login"), EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(), ct);
        SetRefreshCookie(result.RefreshToken);
        return Ok(result.Response);
    }

    [HttpPost("refresh"), EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> Refresh(CancellationToken ct)
    {
        if (!HasTrustedOrigin()) return StatusCode(403, Error("UNTRUSTED_ORIGIN", "This request origin is not allowed."));
        var old = Request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(old)) return Unauthorized(Error("INVALID_SESSION", "Please sign in again."));
        try
        {
            var result = await auth.RefreshAsync(old, HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(), ct);
            SetRefreshCookie(result.RefreshToken);
            return Ok(result.Response);
        }
        catch (ApiException)
        {
            ClearRefreshCookie();
            throw;
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (!HasTrustedOrigin()) return StatusCode(403, Error("UNTRUSTED_ORIGIN", "This request origin is not allowed."));
        if (Request.Cookies.TryGetValue(CookieName, out var token)) await auth.LogoutAsync(token, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    [Authorize, HttpGet("session")]
    public async Task<IActionResult> Session(CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        return Ok(new UserDto(user.UserId, user.Email, user.FirstName, user.LastName,
            user.RoleCode, user.DepartmentId, user.Status, user.MustChangePassword));
    }

    [HttpPost("forgot-password"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken ct)
    {
        await auth.ForgotPasswordAsync(request, ct);
        return Ok(new { message = "If an eligible account exists, a recovery code will be sent." });
    }

    [HttpPost("verify-reset-code"), EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyResetCode(EmailCodeRequest request, CancellationToken ct)
        => Ok(new { resetToken = await auth.VerifyResetCodeAsync(request, ct) });

    [HttpPost("reset-password"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(request, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    [Authorize, HttpPost("change-password"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        await auth.ChangePasswordAsync(id, request.CurrentPassword, request.NewPassword, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    private async Task<Models.Entities.AppUser> CurrentUserAsync(CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return await auth.CurrentUserAsync(id, ct)
            ?? throw new ApiException(401, "INVALID_SESSION", "Please sign in again.");
    }

    private void SetRefreshCookie(string value) => Response.Cookies.Append(CookieName, value, new CookieOptions
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.None, Path = "/api/v1",
        Expires = DateTimeOffset.UtcNow.AddDays(HttpContext.RequestServices
            .GetRequiredService<AuthSettings>().RefreshTokenDays)
    });

    private void ClearRefreshCookie() => Response.Cookies.Delete(CookieName, new CookieOptions
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.None, Path = "/api/v1"
    });

    private bool HasTrustedOrigin()
    {
        if (!Request.Headers.TryGetValue("Origin", out var origin)) return true;
        var allowedOrigins = configuration.GetSection("Frontend:Origins").Get<string[]>() ?? [];
        return allowedOrigins.Contains(origin.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    private ApiError Error(string code, string message) => new(code, message, HttpContext.TraceIdentifier);
}

public sealed record ChangePasswordRequest([Required] string CurrentPassword,
    [Required, MinLength(12)] string NewPassword);
