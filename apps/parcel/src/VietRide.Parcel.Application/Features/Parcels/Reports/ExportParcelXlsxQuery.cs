using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Application.Reporting;

namespace VietRide.Parcel.Application.Features.Parcels.Reports;

public sealed record ExportParcelXlsxQuery(
    Guid OperatorId,
    DateOnly? From,
    DateOnly? To) : IQuery<ExcelReportStream>;
