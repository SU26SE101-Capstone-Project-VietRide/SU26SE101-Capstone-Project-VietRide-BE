using System.Globalization;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Features.Internal.Revenue.RevenueSummary;

internal static class InternalRevenueRangeParser
{
    public static RevenueAnalyticsRange Parse(string? from, string? to)
        => RevenueAnalyticsPeriodRules.AdminRange(ParseDate(from, "from"), ParseDate(to, "to"));

    private static DateOnly? ParseDate(string? value, string field)
    {
        if (value is null)
            return null;
        if (DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            return parsed;
        }

        var message = $"{field} must use YYYY-MM-DD format.";
        throw new CodedValidationException(
            "VALIDATION_ERROR",
            message,
            [new ValidationError(field, message)]);
    }
}
