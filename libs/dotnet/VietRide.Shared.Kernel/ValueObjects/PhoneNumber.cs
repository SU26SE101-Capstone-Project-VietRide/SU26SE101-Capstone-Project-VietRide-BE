using System.Text.RegularExpressions;

namespace VietRide.Shared.Kernel.ValueObjects;

/// <summary>
/// E.164 VN phone number wrapper.
/// Accepted formats: <c>+84xxxxxxxxx</c> (9–10 digits after +84, per schema line 161).
/// Use <see cref="Normalize(string)"/> to convert a local Vietnamese number (0xxxxxxxxx)
/// to E.164 before constructing the value object.
/// </summary>
public readonly record struct PhoneNumber
{
    // Widened per Task 3.4 / Q3: 9 OR 10 digits after +84 (schema CHECK line 161).
    private static readonly Regex E164Vn = new(@"^\+84\d{9,10}$", RegexOptions.Compiled);

    // Local VN format: leading 0 followed by 9 or 10 digits.
    private static readonly Regex LocalVn = new(@"^0[0-9]{9,10}$", RegexOptions.Compiled);

    public string Value { get; }

    private PhoneNumber(string value) => Value = value;

    /// <summary>
    /// Parses an already-normalized E.164 VN phone number (<c>+84xxxxxxxxx</c>).
    /// Throws <see cref="ArgumentException"/> if the format is not E.164 VN.
    /// For user input that may be in local format use <see cref="Normalize(string)"/> first.
    /// </summary>
    public static PhoneNumber Parse(string input)
    {
        var trimmed = input?.Trim() ?? string.Empty;
        if (!E164Vn.IsMatch(trimmed))
            throw new ArgumentException($"Invalid VN phone (expected +84xxxxxxxxx or +84xxxxxxxxxx): {input}", nameof(input));
        return new PhoneNumber(trimmed);
    }

    /// <summary>
    /// Accepts either a local Vietnamese number (<c>0xxxxxxxxx</c>) or an already-normalized
    /// E.164 number (<c>+84xxxxxxxxx</c>) and returns the canonical E.164 form.
    /// Throws <see cref="ArgumentException"/> for any other format.
    /// </summary>
    public static PhoneNumber Normalize(string input)
    {
        var trimmed = input?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("Phone number must not be null or empty.", nameof(input));

        // Already E.164 VN — return as-is.
        if (E164Vn.IsMatch(trimmed))
            return new PhoneNumber(trimmed);

        // Local VN format: strip leading '0', prepend '+84'.
        if (LocalVn.IsMatch(trimmed))
            return new PhoneNumber("+84" + trimmed[1..]);

        throw new ArgumentException($"Invalid VN phone format (expected 0xxxxxxxxx or +84xxxxxxxxx): {input}", nameof(input));
    }

    public static bool TryParse(string input, out PhoneNumber phone)
    {
        try { phone = Parse(input); return true; }
        catch { phone = default; return false; }
    }

    public override string ToString() => Value;
}
