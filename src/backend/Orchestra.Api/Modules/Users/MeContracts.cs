namespace Orchestra.Api.Modules.Users;

public sealed record UpdateMeRequest(
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? PhoneNumber,
    string? JobTitle);

public sealed record MeDto(Guid Id, string Email, string FirstName, string LastName,
    string? DisplayName, string? PhoneNumber, string? JobTitle, string Role,
    Guid? DepartmentId, string Status, bool MustChangePassword, DateTime CreatedAt, DateTime UpdatedAt);
