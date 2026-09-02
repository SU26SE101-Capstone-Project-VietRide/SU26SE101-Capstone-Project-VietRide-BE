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
        var prefix = refundOnly ? "bao-cao-hoan-tien" : "bao-cao-doanh-thu";
        var title = refundOnly ? "Báo cáo hoàn tiền" : "Báo cáo doanh thu";
        var spec = new ExcelReportSpec(
            refundOnly ? "Hoàn tiền" : "Doanh thu",
            ["Mã tham chiếu", "Mã chuyến", "Nội dung nghiệp vụ", "Nguồn phát sinh", "Số tiền", "Thời gian", "Diễn giải", "Mã hệ thống giao dịch", "Mã hệ thống tham chiếu", "Mã hệ thống chuyến"],
            $"{prefix}-{range.FromDate:yyyyMMdd}-{range.ToDate:yyyyMMdd}.xlsx",
            new HashSet<int> { 4 },
            Title: title,
            ReportPeriod: $"{range.FromDate:dd/MM/yyyy} - {range.ToDate:dd/MM/yyyy}",
            ExportedAt: _clock.UtcNow);

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
                ExcelReportCell.TextValue(row.ReferenceCode ?? string.Empty),
                ExcelReportCell.TextValue(row.TripCode
                    ?? (row.TripId.HasValue ? tripCodes.GetValueOrDefault(row.TripId.Value) : null)
                    ?? string.Empty),
                ExcelReportCell.TextValue(PaymentReportLabels.EntryType(row.EntryType)),
                ExcelReportCell.TextValue(PaymentReportLabels.ReferenceType(row.ReferenceType)),
                ExcelReportCell.IntegerValue(row.AmountVnd),
                ExcelReportCell.DateTimeValue(row.OccurredAt),
                ExcelReportCell.TextValue(PaymentReportLabels.Description(
                    row.EntryType,
                    row.AdjustmentReason,
                    row.Note)),
                ExcelReportCell.TextValue(row.EntryId.ToString("D")),
                ExcelReportCell.TextValue(row.ReferenceId.ToString("D")),
                ExcelReportCell.TextValue(row.TripId?.ToString("D") ?? string.Empty),
            ]);
        }
    }
}
