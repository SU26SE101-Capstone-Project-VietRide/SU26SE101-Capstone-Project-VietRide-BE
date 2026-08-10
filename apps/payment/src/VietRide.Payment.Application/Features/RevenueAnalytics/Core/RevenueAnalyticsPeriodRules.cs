using System.Globalization;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public static class RevenueAnalyticsPeriodRules
{
    public const string Timezone = BusinessTime.TimeZoneId;
    private const int MaximumRangeDays = 366;

    public static RevenueAnalyticsRange AdminRange(DateOnly? from, DateOnly? to)
    {
        if (!from.HasValue || !to.HasValue || from > to)
        {
            throw Validation("from", "from and to must define an inclusive date range.");
        }

        var dayCount = to.Value.DayNumber - from.Value.DayNumber + 1;
        if (dayCount is < 1 or > MaximumRangeDays)
        {
            throw Validation("to", "The inclusive date range must contain 1 to 366 days.");
        }

        try
        {
            var toExclusive = to.Value.AddDays(1);
            var previousTo = from.Value;
            var previousFrom = from.Value.AddDays(-dayCount);
            return new RevenueAnalyticsRange(
                from.Value,
                to.Value,
                ToUtc(from.Value),
                ToUtc(toExclusive),
                ToUtc(previousFrom),
                ToUtc(previousTo));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Validation("from", "The date range cannot be represented with its comparison period.");
        }
    }

    public static OperatorRevenuePeriod OperatorMonth(string? month)
        => OperatorPeriod(month, null, null);

    public static OperatorRevenuePeriod OperatorPeriod(string? month, int? year, string? groupBy)
    {
        var hasMonth = !string.IsNullOrWhiteSpace(month);
        var hasYear = year.HasValue;
        if (hasMonth == hasYear)
        {
            throw Validation("month", "Exactly one of month or year is required.");
        }

        if (hasYear)
        {
            if (!string.Equals(groupBy, "month", StringComparison.Ordinal)
                || year is < 2 or > 9998)
            {
                throw Validation("year", "year must use YYYY and groupBy must be month.");
            }

            var yearFirstDay = new DateOnly(year!.Value, 1, 1);
            var currentTo = yearFirstDay.AddYears(1);
            var previousFrom = yearFirstDay.AddYears(-1);
            var months = Enumerable.Range(0, 12)
                .Select(offset => yearFirstDay.AddMonths(offset).ToString("yyyy-MM", CultureInfo.InvariantCulture))
                .ToArray();
            return new OperatorRevenuePeriod(
                true,
                null,
                year,
                yearFirstDay,
                currentTo.AddDays(-1),
                ToUtc(yearFirstDay),
                ToUtc(currentTo),
                ToUtc(previousFrom),
                ToUtc(yearFirstDay),
                ToUtc(previousFrom),
                months);
        }

        if (groupBy is not null)
        {
            throw Validation("groupBy", "groupBy is only valid with year mode.");
        }

        if (month?.Length != 7
            || !DateOnly.TryParseExact(
                $"{month}-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var firstDay))
        {
            throw Validation("month", "month must use YYYY-MM format.");
        }

        try
        {
            var currentTo = firstDay.AddMonths(1);
            var previousFrom = firstDay.AddMonths(-1);
            var twelveMonthFrom = firstDay.AddMonths(-11);
            var months = Enumerable.Range(0, 12)
                .Select(offset => twelveMonthFrom.AddMonths(offset).ToString("yyyy-MM", CultureInfo.InvariantCulture))
                .ToArray();
            return new OperatorRevenuePeriod(
                false,
                month,
                null,
                firstDay,
                currentTo.AddDays(-1),
                ToUtc(firstDay),
                ToUtc(currentTo),
                ToUtc(previousFrom),
                ToUtc(firstDay),
                ToUtc(twelveMonthFrom),
                months);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Validation("month", "month cannot be represented with its required comparison window.");
        }
    }

    public static int ClampTop(int? top) => Math.Clamp(top ?? 5, 1, 20);

    private static DateTimeOffset ToUtc(DateOnly date) =>
        BusinessTime.ToUtc(date, TimeOnly.MinValue);

    private static CodedValidationException Validation(string field, string message)
        => new("VALIDATION_ERROR", message, [new ValidationError(field, message)]);
}
