using MediatR;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.OperatorReports;

public sealed class ExportOperatorLedgerReportQueryHandler
    : IRequestHandler<ExportOperatorLedgerReportQuery, ExcelReportStream>
{
    private readonly IOperatorLedgerEntryRepository _ledger;
    private readonly IExcelReportWriter _writer;
    private readonly IClock _clock;

    public ExportOperatorLedgerReportQueryHandler(
        IOperatorLedgerEntryRepository ledger,
        IExcelReportWriter writer,
        IClock clock)
    {
        _ledger = ledger;
        _writer = writer;
        _clock = clock;
    }

    public Task<ExcelReportStream> Handle(ExportOperatorLedgerReportQuery request, CancellationToken ct)
    {
        var range = OperatorReportRange.Create(request.From, request.To, _clock);
        var refundOnly = request.Kind == OperatorLedgerReportKind.Refunds;
        var prefix = refundOnly ? "refunds" : "revenue";
        var spec = new ExcelReportSpec(
            refundOnly ? "Refunds" : "Revenue",
            ["entry_id", "entry_type", "reference_type", "reference_id", "trip_id", "amount_vnd", "occurred_at", "note"],
            $"{prefix}-report-{range.FromDate:yyyyMMdd}-{range.ToDate:yyyyMMdd}.xlsx",
            new HashSet<int> { 5 });

        return _writer.WriteAsync(spec, ToRowsAsync(request.OperatorId, range, refundOnly, ct), ct);
    }

    private async IAsyncEnumerable<ExcelReportRow> ToRowsAsync(
        Guid operatorId,
        OperatorReportRange range,
        bool refundOnly,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in _ledger
            .StreamOperatorReportRowsAsync(operatorId, range.FromUtc, range.ToUtc, refundOnly, ct)
            .WithCancellation(ct)
            .ConfigureAwait(false))
        {
            yield return new ExcelReportRow([
                ExcelReportCell.TextValue(row.EntryId.ToString("D")),
                ExcelReportCell.TextValue(row.EntryType),
                ExcelReportCell.TextValue(row.ReferenceType),
                ExcelReportCell.TextValue(row.ReferenceId.ToString("D")),
                ExcelReportCell.TextValue(row.TripId?.ToString("D") ?? string.Empty),
                ExcelReportCell.IntegerValue(row.AmountVnd),
                ExcelReportCell.DateTimeValue(row.OccurredAt.UtcDateTime),
                ExcelReportCell.TextValue(row.Note ?? string.Empty),
            ]);
        }
    }
}
