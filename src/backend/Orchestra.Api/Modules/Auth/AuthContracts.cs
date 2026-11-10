using System.ComponentModel.DataAnnotations;

namespace Orchestra.Api.Modules.Auth;

public sealed record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(12)] string Password,
    [Required] string FirstName,
    [Required] string LastName,
    Guid DepartmentId);

public sealed record EmailCodeRequest([Required, EmailAddress] string Email,
    [Required, RegularExpression("^[0-9]{6}$")] string Code);

public sealed record EmailRequest([Required, EmailAddress] string Email);
public sealed record LoginRequest([Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MaxLength(128)] string Password);
public sealed record ResetPasswordRequest([Required] string ResetToken,
    [Required, MinLength(12)] string NewPassword);

public sealed record UserDto(Guid Id, string Email, string FirstName, string LastName,
    string Role, Guid? DepartmentId, string Status, bool MustChangePassword);
public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, UserDto User);
public sealed record SessionDto(Guid Id, DateTime CreatedAt, DateTime ExpiresAt,
    DateTime? LastUsedAt, string? UserAgent, bool IsCurrent);
public sealed record LogoutResponse(string Message);
