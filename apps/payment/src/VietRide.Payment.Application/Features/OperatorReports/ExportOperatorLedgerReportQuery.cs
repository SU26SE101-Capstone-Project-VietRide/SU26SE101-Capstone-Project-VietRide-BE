using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Application.Reporting;

namespace VietRide.Payment.Application.Features.OperatorReports;

public enum OperatorLedgerReportKind
{
    Revenue,
    Refunds,
}

public sealed record ExportOperatorLedgerReportQuery(
    Guid OperatorId,
    DateOnly? From,
    DateOnly? To,
    OperatorLedgerReportKind Kind) : IQuery<ExcelReportStream>;
