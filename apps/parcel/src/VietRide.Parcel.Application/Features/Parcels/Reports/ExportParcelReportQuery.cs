using VietRide.Shared.Application.Cqrs;

namespace VietRide.Parcel.Application.Features.Parcels.Reports;

public sealed record ExportParcelReportQuery(
    Guid OperatorId,
    DateOnly? From,
    DateOnly? To,
    string? Format) : IQuery<ParcelReportExportResponse>;
