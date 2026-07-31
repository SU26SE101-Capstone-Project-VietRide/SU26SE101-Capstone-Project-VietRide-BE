using System.Globalization;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public static class RevenueAnalyticsPeriodRules
{
    public const string Timezone = "Asia/Ho_Chi_Minh";
    private const int MaximumRangeDays = 366;
    private static readonly TimeSpan IctOffset = TimeSpan.FromHours(7);

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
    {
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
                month,
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

    private static DateTimeOffset ToUtc(DateOnly date)
        => new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, IctOffset).ToUniversalTime();

    private static CodedValidationException Validation(string field, string message)
        => new("VALIDATION_ERROR", message, [new ValidationError(field, message)]);
}
