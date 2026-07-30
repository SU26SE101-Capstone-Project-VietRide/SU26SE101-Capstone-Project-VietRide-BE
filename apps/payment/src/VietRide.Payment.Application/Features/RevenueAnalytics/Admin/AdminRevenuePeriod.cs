namespace VietRide.Payment.Application.Features.RevenueAnalytics.Admin;

public sealed record AdminRevenuePeriod(DateOnly From, DateOnly To, string Timezone);
