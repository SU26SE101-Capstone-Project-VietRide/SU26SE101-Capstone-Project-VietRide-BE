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
            "Bưu kiện",
            ["Mã bưu kiện", "Tuyến", "Điểm gửi", "Điểm nhận", "Biển số xe", "Trạng thái", "Kích thước", "Tổng cước", "Tiền cọc", "Phụ thu", "Hoàn tiền", "Thời gian tạo", "Thời gian xác nhận", "Mã hệ thống bưu kiện", "Mã hệ thống chuyến"],
            $"bao-cao-buu-kien-{range.FromDate:yyyyMMdd}-{range.ToDate:yyyyMMdd}.xlsx",
            new HashSet<int> { 7, 8, 9, 10 },
            Title: "Báo cáo bưu kiện",
            ReportPeriod: $"{range.FromDate:dd/MM/yyyy} - {range.ToDate:dd/MM/yyyy}",
            ExportedAt: _clock.UtcNow);

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
                ExcelReportCell.TextValue(row.ParcelCode),
                ExcelReportCell.TextValue(row.RouteName ?? string.Empty),
                ExcelReportCell.TextValue(row.OriginStationName ?? string.Empty),
                ExcelReportCell.TextValue(row.DestinationStationName ?? string.Empty),
                ExcelReportCell.TextValue(row.VehicleLicensePlate ?? string.Empty),
                ExcelReportCell.TextValue(ParcelReportLabels.Status(row.Status)),
                ExcelReportCell.TextValue(ParcelReportLabels.Size(row.SizeCategory)),
                ExcelReportCell.IntegerValue(row.TotalPriceVnd),
                ExcelReportCell.IntegerValue(row.DepositAmountVnd),
                ExcelReportCell.IntegerValue(row.AdditionalAmountVnd),
                ExcelReportCell.IntegerValue(row.RefundAmountVnd),
                ExcelReportCell.DateTimeValue(row.CreatedAt),
                row.ConfirmedAt.HasValue
                    ? ExcelReportCell.DateTimeValue(row.ConfirmedAt.Value)
                    : ExcelReportCell.BlankValue(),
                ExcelReportCell.TextValue(row.ParcelId.ToString("D")),
                ExcelReportCell.TextValue(row.TripId.ToString("D")),
            ]);
        }
    }
}
