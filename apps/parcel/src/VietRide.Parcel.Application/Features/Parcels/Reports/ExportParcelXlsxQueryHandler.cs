using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.Reports;

public sealed class ExportParcelXlsxQueryHandler
    : IRequestHandler<ExportParcelXlsxQuery, ExcelReportStream>
{
    private readonly IParcelRepository _parcels;
    private readonly IExcelReportWriter _writer;
    private readonly IClock _clock;

    public ExportParcelXlsxQueryHandler(
        IParcelRepository parcels,
        IExcelReportWriter writer,
        IClock clock)
    {
        _parcels = parcels;
        _writer = writer;
        _clock = clock;
    }

    public Task<ExcelReportStream> Handle(ExportParcelXlsxQuery request, CancellationToken ct)
    {
        var range = OperatorReportRange.Create(request.From, request.To, _clock);
        var spec = new ExcelReportSpec(
            "Parcels",
            ["parcel_id", "parcel_code", "trip_id", "status", "size_category", "total_price_vnd", "deposit_amount_vnd", "additional_amount_vnd", "refund_amount_vnd", "created_at_asia_ho_chi_minh", "confirmed_at_asia_ho_chi_minh"],
            $"parcels-report-{range.FromDate:yyyyMMdd}-{range.ToDate:yyyyMMdd}.xlsx",
            new HashSet<int> { 5, 6, 7, 8 });

        return _writer.WriteAsync(spec, ToRowsAsync(request.OperatorId, range, ct), ct);
    }

    private async IAsyncEnumerable<ExcelReportRow> ToRowsAsync(
        Guid operatorId,
        OperatorReportRange range,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in _parcels
            .StreamOperatorReportRowsAsync(operatorId, range.FromUtc, range.ToUtc, ct)
            .WithCancellation(ct)
            .ConfigureAwait(false))
        {
            yield return new ExcelReportRow([
                ExcelReportCell.TextValue(row.ParcelId.ToString("D")),
                ExcelReportCell.TextValue(row.ParcelCode),
                ExcelReportCell.TextValue(row.TripId.ToString("D")),
                ExcelReportCell.TextValue(row.Status),
                ExcelReportCell.TextValue(row.SizeCategory),
                ExcelReportCell.IntegerValue(row.TotalPriceVnd),
                ExcelReportCell.IntegerValue(row.DepositAmountVnd),
                ExcelReportCell.IntegerValue(row.AdditionalAmountVnd),
                ExcelReportCell.IntegerValue(row.RefundAmountVnd),
                ExcelReportCell.DateTimeValue(row.CreatedAt),
                row.ConfirmedAt.HasValue
                    ? ExcelReportCell.DateTimeValue(row.ConfirmedAt.Value)
                    : ExcelReportCell.BlankValue(),
            ]);
        }
    }
}
