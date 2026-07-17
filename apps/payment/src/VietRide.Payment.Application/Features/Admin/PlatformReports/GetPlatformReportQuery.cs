using VietRide.Shared.Application.Cqrs;

namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed record GetPlatformReportQuery(string? From, string? To)
    : IQuery<PlatformReportResult>;
