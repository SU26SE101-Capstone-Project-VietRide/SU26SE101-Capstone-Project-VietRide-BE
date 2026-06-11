namespace VietRide.Booking.Domain.ValueObjects;

/// <summary>
/// Booking code in the format <c>VR-yyyyMMdd-XXXXXXXX</c>
/// where XXXXXXXX is an 8-character uppercase Base32 token.
/// QR code encodes this string directly.
/// </summary>
public readonly record struct BookingCode
{
    // Base32 alphabet (Crockford — uppercase, no I/L/O/U to avoid confusion)
    private const string Base32Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public string Value { get; }

    private BookingCode(string value) => Value = value;

    /// <summary>
    /// Generates a new unique booking code using the current UTC date and a random 8-char Base32 suffix.
    /// </summary>
    public static BookingCode Generate(DateTimeOffset utcNow)
    {
        var datePart = utcNow.UtcDateTime.ToString("yyyyMMdd");
        var randomPart = GenerateBase32(8);
        return new BookingCode($"VR-{datePart}-{randomPart}");
    }

    /// <summary>
    /// Parses a booking code string. Throws <see cref="ArgumentException"/> if the format is invalid.
    /// </summary>
    public static BookingCode Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Booking code cannot be null or whitespace.", nameof(value));

        // Expected: VR-yyyyMMdd-XXXXXXXX (total length 20: VR-(3) + yyyyMMdd(8) + -(1) + 8chars)
        if (!value.StartsWith("VR-", StringComparison.Ordinal) || value.Length != 20)
            throw new ArgumentException($"Booking code '{value}' is not in the expected format VR-yyyyMMdd-XXXXXXXX.", nameof(value));

        if (value[11] != '-')
            throw new ArgumentException($"Booking code '{value}' is not in the expected format VR-yyyyMMdd-XXXXXXXX.", nameof(value));

        if (!DateTime.TryParseExact(value[3..11], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out _))
            throw new ArgumentException($"Booking code '{value}' has an invalid date part.", nameof(value));

        return new BookingCode(value);
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
