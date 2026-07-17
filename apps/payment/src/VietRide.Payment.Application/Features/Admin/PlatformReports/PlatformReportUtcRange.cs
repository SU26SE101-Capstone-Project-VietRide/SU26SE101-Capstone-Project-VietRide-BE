using System.Globalization;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

internal sealed record PlatformReportUtcRange(DateTimeOffset From, DateTimeOffset To)
{
    private static readonly string[] Rfc3339UtcFormats = BuildFormats();

    public static PlatformReportUtcRange Parse(string? from, string? to)
    {
        var fromUtc = ParseRequiredUtc(from, "from");
        var toUtc = ParseRequiredUtc(to, "to");
        if (fromUtc >= toUtc)
        {
            throw Validation("from must be earlier than to.");
        }

        if (toUtc - fromUtc > TimeSpan.FromDays(366))
        {
            throw Validation("The report range cannot exceed 366 days.");
        }

        return new PlatformReportUtcRange(fromUtc, toUtc);
    }

    private static DateTimeOffset ParseRequiredUtc(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !value.EndsWith('Z')
            || !DateTimeOffset.TryParseExact(
                value,
                Rfc3339UtcFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw Validation($"{field} must be an RFC 3339 UTC timestamp ending in Z.");
        }

        return parsed;
    }

    private static CodedValidationException Validation(string message)
        => new("VALIDATION_ERROR", message);

    private static string[] BuildFormats()
        => Enumerable.Range(0, 8)
            .Select(fractionDigits => fractionDigits == 0
                ? "yyyy-MM-dd'T'HH:mm:ss'Z'"
                : $"yyyy-MM-dd'T'HH:mm:ss.{new string('F', fractionDigits)}'Z'")
            .ToArray();
}
