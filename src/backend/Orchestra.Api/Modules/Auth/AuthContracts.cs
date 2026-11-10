using System.ComponentModel.DataAnnotations;

namespace Orchestra.Api.Modules.Auth;

public sealed record RegisterRequest(
    [Required(ErrorMessage = "Email is required."), EmailAddress(ErrorMessage = "Please enter a valid email address.")] string Email,
    [Required(ErrorMessage = "Password is required."), MinLength(12, ErrorMessage = "Password must be at least 12 characters.")] string Password,
    [Required(ErrorMessage = "First name is required.")] string FirstName,
    [Required(ErrorMessage = "Last name is required.")] string LastName,
    Guid DepartmentId);

public sealed record EmailCodeRequest(
    [Required(ErrorMessage = "Email is required."), EmailAddress(ErrorMessage = "Please enter a valid email address.")] string Email,
    [Required(ErrorMessage = "Code is required."), RegularExpression("^[0-9]{6}$", ErrorMessage = "Code must be exactly 6 digits.")] string Code);

public sealed record EmailRequest(
    [Required(ErrorMessage = "Email is required."), EmailAddress(ErrorMessage = "Please enter a valid email address.")] string Email);
public sealed record LoginRequest(
    [Required(ErrorMessage = "Email is required."), EmailAddress(ErrorMessage = "Please enter a valid email address.")] string Email,
    [Required(ErrorMessage = "Password is required.")] string Password);
public sealed record ResetPasswordRequest(
    [Required(ErrorMessage = "Reset token is required.")] string ResetToken,
    [Required(ErrorMessage = "New password is required."), MinLength(12, ErrorMessage = "Password must be at least 12 characters.")] string NewPassword);
public sealed record ChangePasswordRequest(
    [Required(ErrorMessage = "Current password is required.")] string CurrentPassword,
    [Required(ErrorMessage = "New password is required."), MinLength(12, ErrorMessage = "Password must be at least 12 characters.")] string NewPassword);

public sealed record UserDto(Guid Id, string Email, string FirstName, string LastName,
    string Role, Guid? DepartmentId, string Status, bool MustChangePassword);
public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, UserDto User);
public sealed record SessionDto(Guid Id, DateTime CreatedAt, DateTime ExpiresAt,
    DateTime? LastUsedAt, string? UserAgent, bool IsCurrent);
