using VietRide.Booking.Application.Abstractions.Services;

namespace VietRide.Booking.Application.Services;

/// <summary>
/// Generates 8-character uppercase base32 voucher codes (v7:4564).
/// Stateless — no repo/EF dependency. Uniqueness enforcement is the caller's responsibility
/// (handler retries or catches VOUCHER_CODE_CONFLICT on duplicate-key from the DB).
/// </summary>
public sealed class VoucherCodeGenerator : IVoucherCodeGenerator
{
    // Base32 alphabet: RFC 4648 §6 uppercase (A–Z + 2–7), avoids ambiguous 0/O/1/I.
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int CodeLength = 8;

    /// <inheritdoc/>
    public string Generate()
    {
        var bytes = new byte[CodeLength];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);

        var chars = new char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return new string(chars);
    }
}
