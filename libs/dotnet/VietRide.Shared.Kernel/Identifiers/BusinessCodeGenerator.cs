using System.Security.Cryptography;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Shared.Kernel.Identifiers;

public static class BusinessCodeGenerator
{
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int TokenLength = 8;

    public static string Generate(string prefix, DateTimeOffset businessInstant)
    {
        ValidatePrefix(prefix);
        var businessDate = BusinessTime.ToLocalDate(businessInstant);
        Span<byte> randomBytes = stackalloc byte[TokenLength];
        RandomNumberGenerator.Fill(randomBytes);
        Span<char> token = stackalloc char[TokenLength];
        for (var index = 0; index < token.Length; index++)
        {
            token[index] = CrockfordAlphabet[randomBytes[index] & 31];
        }

        return $"{prefix}-{businessDate:yyyyMMdd}-{new string(token)}";
    }

    private static void ValidatePrefix(string prefix)
    {
        if (prefix.Length is < 2 or > 4
            || prefix.Any(character => character is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9')))
        {
            throw new ArgumentException("Business code prefix must contain 2 to 4 uppercase letters or digits.", nameof(prefix));
        }
    }
}
