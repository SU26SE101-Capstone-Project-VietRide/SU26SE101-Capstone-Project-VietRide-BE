using System.Text.RegularExpressions;

namespace VietRide.Shared.Kernel.ValueObjects;

/// E.164 VN phone number wrapper. Expected format: "+84xxxxxxxxx" (10 digits after +84).
public readonly record struct PhoneNumber
{
    private static readonly Regex E164Vn = new(@"^\+84\d{9}$", RegexOptions.Compiled);

    public string Value { get; }

    private PhoneNumber(string value) => Value = value;

    public static PhoneNumber Parse(string input)
    {
        var trimmed = input?.Trim() ?? string.Empty;
        if (!E164Vn.IsMatch(trimmed))
            throw new ArgumentException($"Invalid VN phone (expected +84xxxxxxxxx): {input}", nameof(input));
        return new PhoneNumber(trimmed);
    }

    public static bool TryParse(string input, out PhoneNumber phone)
    {
        try { phone = Parse(input); return true; }
        catch { phone = default; return false; }
    }

    public override string ToString() => Value;
}
