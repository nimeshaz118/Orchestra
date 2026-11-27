using System.Security.Cryptography;
using System.Text;

namespace Orchestra.Api.Modules.Auth;

public sealed class CodeService(AuthSettings settings)
{
    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public string Hash(Guid userId, string purpose, string code)
    {
        var bytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.CodeHashKey!),
            Encoding.UTF8.GetBytes($"{userId:N}:{purpose}:{code}"));
        return Convert.ToHexStringLower(bytes);
    }

    public bool Matches(Guid userId, string purpose, string code, string expected)
    {
        if (expected.Length != 64) return false;
        var actual = Convert.FromHexString(Hash(userId, purpose, code));
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expected));
    }
}
