namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed record PlatformReportPeriod(DateTimeOffset From, DateTimeOffset To, string Timezone);
