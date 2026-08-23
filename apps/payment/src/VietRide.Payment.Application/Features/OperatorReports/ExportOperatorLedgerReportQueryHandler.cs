using MediatR;
using VietRide.Payment.Application.Abstractions.ExternalClients;
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
    private readonly ITripRevenueAnalyticsClient? _trips;

    public ExportOperatorLedgerReportQueryHandler(
        IOperatorLedgerEntryRepository ledger,
        IExcelReportWriter writer,
        IClock clock,
        ITripRevenueAnalyticsClient? trips = null)
    {
        _ledger = ledger;
        _writer = writer;
        _clock = clock;
        _trips = trips;
    }

    public async Task<ExcelReportStream> Handle(ExportOperatorLedgerReportQuery request, CancellationToken ct)
    {
        var range = OperatorReportRange.Create(request.From, request.To, _clock);
        var refundOnly = request.Kind == OperatorLedgerReportKind.Refunds;
        var prefix = refundOnly ? "refunds" : "revenue";
        var spec = new ExcelReportSpec(
            refundOnly ? "Refunds" : "Revenue",
            ["entry_id", "reference_code", "trip_code", "entry_type", "reference_type", "reference_id", "trip_id", "amount_vnd", "occurred_at_asia_ho_chi_minh", "note"],
            $"{prefix}-report-{range.FromDate:yyyyMMdd}-{range.ToDate:yyyyMMdd}.xlsx",
            new HashSet<int> { 7 });

        var tripCodes = await LoadTripCodesSafeAsync(request.OperatorId, range, ct);
        return await _writer.WriteAsync(spec, ToRowsAsync(request.OperatorId, range, refundOnly, tripCodes, ct), ct);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadTripCodesSafeAsync(
        Guid operatorId,
        OperatorReportRange range,
        CancellationToken cancellationToken)
    {
        if (_trips is null)
            return new Dictionary<Guid, string>();

        var tripIds = await _ledger.ListOperatorReportTripIdsAsync(
            operatorId,
            range.FromUtc,
            range.ToUtc,
            cancellationToken);
        if (tripIds.Count == 0)
            return new Dictionary<Guid, string>();

        try
        {
            var summaries = await _trips.GetTripSummariesAsync(tripIds, cancellationToken);
            return summaries
                .Where(item => item.TripCode is not null)
                .ToDictionary(item => item.TripId, item => item.TripCode!);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new Dictionary<Guid, string>();
        }
    }

    private async IAsyncEnumerable<ExcelReportRow> ToRowsAsync(
        Guid operatorId,
        OperatorReportRange range,
        bool refundOnly,
        IReadOnlyDictionary<Guid, string> tripCodes,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in _ledger
            .StreamOperatorReportRowsAsync(operatorId, range.FromUtc, range.ToUtc, refundOnly, ct)
            .WithCancellation(ct)
            .ConfigureAwait(false))
        {
            yield return new ExcelReportRow([
                ExcelReportCell.TextValue(row.EntryId.ToString("D")),
                ExcelReportCell.TextValue(row.ReferenceCode ?? string.Empty),
                ExcelReportCell.TextValue(row.TripCode
                    ?? (row.TripId.HasValue ? tripCodes.GetValueOrDefault(row.TripId.Value) : null)
                    ?? string.Empty),
                ExcelReportCell.TextValue(row.EntryType),
                ExcelReportCell.TextValue(row.ReferenceType),
                ExcelReportCell.TextValue(row.ReferenceId.ToString("D")),
                ExcelReportCell.TextValue(row.TripId?.ToString("D") ?? string.Empty),
                ExcelReportCell.IntegerValue(row.AmountVnd),
                ExcelReportCell.DateTimeValue(row.OccurredAt),
                ExcelReportCell.TextValue(row.Note ?? string.Empty),
            ]);
        }
    }
}
