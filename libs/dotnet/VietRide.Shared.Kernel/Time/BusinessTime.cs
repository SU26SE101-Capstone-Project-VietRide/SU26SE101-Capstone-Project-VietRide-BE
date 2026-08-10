namespace VietRide.Shared.Kernel.Time;

/// <summary>
/// Converts between UTC instants and VietRide's single business calendar timezone.
/// Persistence, internal HTTP, and event timestamps remain UTC. FE-facing responses may use this
/// timezone for presentation without changing the represented instant.
/// </summary>
public static class BusinessTime
{
    public const string TimeZoneId = "Asia/Ho_Chi_Minh";

    private static readonly Lazy<TimeZoneInfo> TimeZone = new(
        () => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureTimeZoneAvailable() => _ = TimeZone.Value;

    public static DateTimeOffset ToUtc(DateOnly localDate, TimeOnly localTime)
    {
        var localDateTime = DateTime.SpecifyKind(
            localDate.ToDateTime(localTime),
            DateTimeKind.Unspecified);

        return new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(localDateTime, TimeZone.Value),
            TimeSpan.Zero);
    }

    public static DateTimeOffset NormalizeUtc(DateTimeOffset instant) => instant.ToUniversalTime();

    public static DateTimeOffset ToLocalOffset(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZone.Value);

    public static DateTime ToLocalDateTime(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZone.Value).DateTime;

    public static DateOnly ToLocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(ToLocalDateTime(instant));

    public static UtcRange GetUtcDayRange(DateOnly localDate) =>
        new(
            ToUtc(localDate, TimeOnly.MinValue),
            ToUtc(localDate.AddDays(1), TimeOnly.MinValue));

    public static UtcRange GetUtcRange(DateOnly fromLocalDate, DateOnly toLocalDateInclusive)
    {
        if (toLocalDateInclusive < fromLocalDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toLocalDateInclusive),
                "The end date must be on or after the start date.");
        }

        return new UtcRange(
            ToUtc(fromLocalDate, TimeOnly.MinValue),
            ToUtc(toLocalDateInclusive.AddDays(1), TimeOnly.MinValue));
    }

    public static int ToIsoDayOfWeek(DateOnly localDate) =>
        localDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)localDate.DayOfWeek;
}

public readonly record struct UtcRange(DateTimeOffset FromUtc, DateTimeOffset ToUtcExclusive);
