using VietRide.Shared.Application.Cqrs;

namespace VietRide.Parcel.Application.Features.Parcels.Reports;

public sealed record GetParcelReportSummaryQuery(
    Guid OperatorId,
    DateOnly? From,
    DateOnly? To) : IQuery<ParcelReportSummaryResponse>;
