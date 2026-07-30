using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.BookingStats;

internal static class BookingStatsQueryRules
{
    internal const string DateGroup = "date";
    internal const string MonthGroup = "month";
    internal const string OperatorGroup = "operator";
    private const int MaximumInclusiveDays = 366;

    internal static void ValidateRange(DateOnly? from, DateOnly? to, bool requireCompleteRange)
    {
        if (requireCompleteRange && (!from.HasValue || !to.HasValue))
        {
            var errors = new List<ValidationError>();
            if (!from.HasValue)
            {
                errors.Add(new ValidationError("from", "from is required for groupBy=month."));
            }

            if (!to.HasValue)
            {
                errors.Add(new ValidationError("to", "to is required for groupBy=month."));
            }

            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "from and to are required for monthly booking stats.",
                errors);
        }

        if (!from.HasValue || !to.HasValue)
        {
            return;
        }

        if (from.Value > to.Value)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "from must be on or before to.",
                [new ValidationError("from", "from must be on or before to.")]);
        }

        var inclusiveDays = to.Value.DayNumber - from.Value.DayNumber + 1;
        if (inclusiveDays > MaximumInclusiveDays)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Booking stats range cannot exceed 366 inclusive days.",
                [new ValidationError("to", "The inclusive date range cannot exceed 366 days.")]);
        }
    }

    internal static IEnumerable<DateOnly> EnumerateMonthStarts(DateOnly from, DateOnly to)
    {
        var current = new DateOnly(from.Year, from.Month, 1);
        var final = new DateOnly(to.Year, to.Month, 1);

        while (true)
        {
            yield return current;
            if (current == final)
            {
                yield break;
            }

            current = current.AddMonths(1);
        }
    }
}
