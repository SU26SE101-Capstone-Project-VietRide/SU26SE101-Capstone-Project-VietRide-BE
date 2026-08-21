namespace VietRide.Parcel.Application.Services;

public static class BusinessDayDeadline
{
    public static DateTimeOffset Add(DateTimeOffset start, int businessDays)
    {
        var result = start;
        var remaining = Math.Max(0, businessDays);
        while (remaining > 0)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                remaining--;
        }

        return result;
    }
}
