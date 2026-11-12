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
public sealed class MeController(AuthService auth, MeService me, AvatarService avatars) : ControllerBase
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

    [HttpPut("avatar")]
    [RequestSizeLimit(AvatarValidator.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = AvatarValidator.MaxRequestBytes,
        MemoryBufferThreshold = AvatarValidator.MaxRequestBytes)]
    public async Task<IActionResult> UploadAvatar()
    {
        var ct = HttpContext.RequestAborted;
        if (Request.ContentLength > AvatarValidator.MaxRequestBytes) throw AvatarValidator.TooLarge();
        if (!Request.HasFormContentType || !Request.ContentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw new ApiException(400, "INVALID_AVATAR", "Send a multipart form with one file named 'file'.");

        // Bound chunked requests too, before the multipart parser buffers files.
        var bytes = new byte[AvatarValidator.MaxRequestBytes + 1];
        int length;
        try { length = await Request.Body.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, ct); }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413) { throw AvatarValidator.TooLarge(); }
        if (length > AvatarValidator.MaxRequestBytes) throw AvatarValidator.TooLarge();

        var originalBody = Request.Body;
        using var body = new MemoryStream(bytes, 0, length);
        try
        {
            Request.Body = body;
            IFormCollection form;
            try { form = await Request.ReadFormAsync(ct); }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            { throw new ApiException(400, "INVALID_AVATAR", "The multipart form is invalid or incomplete."); }
            if (form.Files.Count != 1 || form.Files[0].Name != "file")
                throw new ApiException(400, "INVALID_AVATAR", "Send exactly one file named 'file'.");

            var id = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
            await avatars.SaveAsync(id, form.Files[0], ct);
        }
        finally { Request.Body = originalBody; }
        return Ok(new { avatarUrl = "/api/v1/me/avatar" });
    }

    [HttpGet("avatar")]
    public async Task<IActionResult> Avatar(CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(await avatars.ReadAsync(id, ct), "image/png");
    }

    [HttpDelete("avatar")]
    public async Task<IActionResult> DeleteAvatar(CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        await avatars.DeleteAsync(id, ct);
        return NoContent();
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
