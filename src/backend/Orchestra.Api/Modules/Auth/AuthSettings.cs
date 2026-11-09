namespace Orchestra.Api.Modules.Auth;

public sealed class AuthSettings
{
    public string Issuer { get; set; } = "Orchestra.Api";
    public string Audience { get; set; } = "Orchestra.Web";
    public string? SigningKey { get; set; }
    public string? CodeHashKey { get; set; }
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
    public int ResetTokenMinutes { get; set; } = 10;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SigningKey) || SigningKey.Length < 32 ||
            string.IsNullOrWhiteSpace(CodeHashKey) || CodeHashKey.Length < 32 ||
            AccessTokenMinutes is < 1 or > 60 || RefreshTokenDays is < 1 or > 90 ||
            ResetTokenMinutes is < 1 or > 30)
            throw new InvalidOperationException("Configure Auth:SigningKey and Auth:CodeHashKey (at least 32 characters each), and valid token lifetimes in User Secrets.");
    }
}
