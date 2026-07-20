using System.Globalization;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Bookings.History;

internal sealed record BookingHistoryDateRange(DateTimeOffset? From, DateTimeOffset? To)
{
    public static BookingHistoryDateRange Parse(string? from, string? to)
    {
        var parsedFrom = ParseOptional(from, "from");
        var parsedTo = ParseOptional(to, "to");
        if (parsedFrom.HasValue && parsedTo.HasValue && parsedFrom.Value >= parsedTo.Value)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "from must be earlier than to.",
                [new ValidationError("from", "from must be earlier than to.")]);
        }

        return new BookingHistoryDateRange(parsedFrom, parsedTo);
    }

    public static bool IsOptionalRfc3339(string? value)
        => value is null || TryParse(value, out _);

    public static bool IsOrdered(string? from, string? to)
        => !TryParse(from, out var parsedFrom)
            || !TryParse(to, out var parsedTo)
            || parsedFrom < parsedTo;

    private static DateTimeOffset? ParseOptional(string? value, string field)
    {
        if (value is null)
            return null;
        if (TryParse(value, out var parsed))
            return parsed;

        throw new CodedValidationException(
            "VALIDATION_ERROR",
            $"{field} must be an RFC 3339 timestamp.",
            [new ValidationError(field, $"{field} must be an RFC 3339 timestamp.")]);
    }

    private static bool TryParse(string? value, out DateTimeOffset parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('T', StringComparison.Ordinal))
            return false;

        var hasZone = value.EndsWith('Z')
            || (value.Length >= 6
                && (value[^6] == '+' || value[^6] == '-')
                && value[^3] == ':');
        if (!hasZone
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsed))
        {
            return false;
        }

        parsed = parsed.ToUniversalTime();
        return true;
    }
}
