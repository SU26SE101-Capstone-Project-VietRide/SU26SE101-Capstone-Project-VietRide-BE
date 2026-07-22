using MediatR;

namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed record GetPlatformLedgerReportQuery(
    string? From,
    string? To) : IRequest<PlatformLedgerReportResult>;
