using System.Globalization;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Booking.Application.Features.Admin.PlatformReports;

internal sealed record PlatformReportBusinessDateRange(
    DateOnly From,
    DateOnly To,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc)
{
    public static PlatformReportBusinessDateRange Parse(string? from, string? to)
    {
        var fromDate = ParseRequiredDate(from, "from");
        var toDate = ParseRequiredDate(to, "to");
        if (fromDate > toDate)
            throw Validation("from must be on or before to.");

        var inclusiveDays = toDate.DayNumber - fromDate.DayNumber + 1;
        if (inclusiveDays > 366)
            throw Validation("The inclusive report range cannot exceed 366 days.");

        try
        {
            return new PlatformReportBusinessDateRange(
                fromDate,
                toDate,
                BusinessTime.ToUtc(fromDate, TimeOnly.MinValue),
                BusinessTime.ToUtc(toDate.AddDays(1), TimeOnly.MinValue));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Validation("The inclusive report range cannot be represented.");
        }
    }

    private static DateOnly ParseRequiredDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw Validation($"{field} must use YYYY-MM-DD format.");
        }

        return parsed;
    }

    private static CodedValidationException Validation(string message)
        => new("VALIDATION_ERROR", message);
}
