using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Application.Reporting;

namespace VietRide.Trip.Application.Features.OperatorReports;

public sealed record ExportOccupancyReportQuery(
    Guid OperatorId,
    DateOnly? From,
    DateOnly? To) : IQuery<ExcelReportStream>;
