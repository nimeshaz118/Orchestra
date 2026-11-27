namespace Orchestra.Api.Modules.Users;

/// <summary>
/// An uploaded avatar file as plain values, so the business layer does not depend on ASP.NET Core's IFormFile.
/// The caller owns <see cref="Content"/> and is responsible for disposing it.
/// </summary>
public sealed record AvatarUpload(string FileName, string ContentType, long Length, Stream Content);
