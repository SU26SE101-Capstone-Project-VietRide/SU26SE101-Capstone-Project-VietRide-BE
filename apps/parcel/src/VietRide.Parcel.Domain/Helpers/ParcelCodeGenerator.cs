using System.Security.Cryptography;

namespace VietRide.Parcel.Domain.Helpers;

/// <summary>
/// Generates the plain parcel code encoded directly by the client-rendered QR image.
/// </summary>
public static class ParcelCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int SuffixLength = 8;

    public static string Generate(DateOnly date)
    {
        Span<char> suffix = stackalloc char[SuffixLength];
        for (var i = 0; i < SuffixLength; i++)
        {
            suffix[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"VR-PCL-{date:yyyyMMdd}-{suffix.ToString()}";
    }

    public static string Generate(DateTimeOffset now)
        => Generate(VietRide.Shared.Kernel.Time.BusinessTime.ToLocalDate(now));
}
