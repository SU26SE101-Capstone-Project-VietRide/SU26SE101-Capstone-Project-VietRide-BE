using VietRide.Shared.Application.Cqrs;

namespace VietRide.Parcel.Application.Features.Internal.Reports.PlatformParcels;

public sealed record GetPlatformParcelReportQuery(string? From, string? To)
    : IQuery<PlatformParcelReportResult>;
