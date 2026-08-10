using VietRide.Shared.Kernel.Time;

namespace VietRide.Booking.Domain.ValueObjects;

/// <summary>
/// Per-seat ticket code in the format <c>VT-yyyyMMdd-XXXXXXXX</c>.
/// QR code encodes this string directly for ticket-level boarding.
/// </summary>
public readonly record struct TicketCode
{
    private const string Base32Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public string Value { get; }

    private TicketCode(string value) => Value = value;

    public static TicketCode Generate(DateTimeOffset utcNow)
    {
        var datePart = BusinessTime.ToLocalDate(utcNow).ToString("yyyyMMdd");
        return new TicketCode($"VT-{datePart}-{GenerateBase32(8)}");
    }

    public static TicketCode Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Ticket code cannot be null or whitespace.", nameof(value));
        }

        if (!value.StartsWith("VT-", StringComparison.Ordinal) || value.Length != 20 || value[11] != '-')
        {
            throw new ArgumentException(
                $"Ticket code '{value}' is not in the expected format VT-yyyyMMdd-XXXXXXXX.",
                nameof(value));
        }

        if (!DateTime.TryParseExact(
            value[3..11],
            "yyyyMMdd",
            null,
            System.Globalization.DateTimeStyles.None,
            out _))
        {
            throw new ArgumentException($"Ticket code '{value}' has an invalid date part.", nameof(value));
        }

        return new TicketCode(value);
    }

    public override string ToString() => Value;

    private static string GenerateBase32(int length)
    {
        var bytes = new byte[length];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);

        var result = new char[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = Base32Alphabet[bytes[i] % 32];
        }

        return new string(result);
    }
}
