using System.Globalization;

namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public static class RevenueReportCacheKeys
{
    public static readonly TimeSpan Expiration = TimeSpan.FromSeconds(60);
    private const string Version = "revenue:v2";

    public static string AdminAnalytics(RevenueAnalyticsRange range, int top)
        => FormattableString.Invariant(
            $"{Version}:admin:analytics:{range.FromUtc.UtcTicks}:{range.ToUtc.UtcTicks}:top:{top}");

    public static string OperatorAnalytics(Guid operatorId, OperatorRevenuePeriod period)
        => FormattableString.Invariant(
            $"{Version}:operator:{operatorId:D}:analytics:{period.QueryFromUtc.UtcTicks}:{period.CurrentToUtc.UtcTicks}");

    public static string InternalAdminSummary(RevenueAnalyticsRange range)
        => FormattableString.Invariant(
            $"{Version}:internal:admin-summary:{range.FromUtc.UtcTicks}:{range.ToUtc.UtcTicks}");

    public static string InternalOperatorSummary(Guid operatorId, RevenueAnalyticsRange range)
        => FormattableString.Invariant(
            $"{Version}:internal:operator:{operatorId:D}:summary:{range.FromUtc.UtcTicks}:{range.ToUtc.UtcTicks}");
}
