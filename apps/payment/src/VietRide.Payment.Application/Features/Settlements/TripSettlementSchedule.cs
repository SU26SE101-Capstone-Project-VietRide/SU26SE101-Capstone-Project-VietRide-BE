using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Application.Features.Settlements;

public static class TripSettlementSchedule
{
    public const int HoldDays = 7;
    public const string EligibilityCron = "0 19 * * *";
    public const string AutoSettlementCron = "0 2 * * 1";

    public static readonly TimeZoneInfo TimeZone = TimeZoneInfo.Utc;

    public static DateTimeOffset GetNextEligibilitySweepAtOrAfter(DateTimeOffset notBefore)
    {
        var utc = notBefore.ToUniversalTime();
        var candidate = new DateTimeOffset(
            utc.Year,
            utc.Month,
            utc.Day,
            19,
            0,
            0,
            TimeSpan.Zero);
        return candidate < utc ? candidate.AddDays(1) : candidate;
    }

    public static DateTimeOffset GetNextAutoSettlementAfter(DateTimeOffset after)
    {
        var utc = after.ToUniversalTime();
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)utc.DayOfWeek + 7) % 7;
        var candidateDate = utc.Date.AddDays(daysUntilMonday);
        var candidate = new DateTimeOffset(
            candidateDate.Year,
            candidateDate.Month,
            candidateDate.Day,
            2,
            0,
            0,
            TimeSpan.Zero);
        return candidate <= utc ? candidate.AddDays(7) : candidate;
    }

    public static DateTimeOffset? GetNextScheduledAttemptAt(
        OperatorTripSettlementStatus status,
        DateTimeOffset eligibleAt,
        DateTimeOffset now)
        => status switch
        {
            OperatorTripSettlementStatus.PENDING_HOLD => GetNextAutoSettlementAfter(
                GetNextEligibilitySweepAtOrAfter(eligibleAt > now ? eligibleAt : now)),
            OperatorTripSettlementStatus.ELIGIBLE => GetNextAutoSettlementAfter(now),
            _ => null,
        };
}
