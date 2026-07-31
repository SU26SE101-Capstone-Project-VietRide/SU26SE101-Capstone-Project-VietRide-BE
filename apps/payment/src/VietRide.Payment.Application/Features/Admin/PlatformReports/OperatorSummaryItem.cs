namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed record OperatorSummaryItem(Guid OperatorId, string OperatorName, string? LogoUrl = null);
