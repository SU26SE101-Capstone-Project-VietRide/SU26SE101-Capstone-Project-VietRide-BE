using MediatR;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.OperatorReports;

public sealed class ExportOccupancyReportQueryHandler
    : IRequestHandler<ExportOccupancyReportQuery, ExcelReportStream>
{
    private readonly ITripRepository _trips;
    private readonly IExcelReportWriter _writer;
    private readonly IClock _clock;

    public ExportOccupancyReportQueryHandler(
        ITripRepository trips,
        IExcelReportWriter writer,
        IClock clock)
    {
        _trips = trips;
        _writer = writer;
        _clock = clock;
    }

    public Task<ExcelReportStream> Handle(ExportOccupancyReportQuery request, CancellationToken ct)
    {
        var range = OperatorReportRange.Create(request.From, request.To, _clock);
        var spec = new ExcelReportSpec(
            "Tỷ lệ lấp đầy",
            ["Mã chuyến", "Tuyến", "Biển số xe", "Trạng thái", "Thời gian khởi hành", "Ghế mở bán", "Ghế đã đặt", "Tỷ lệ lấp đầy", "Mã hệ thống chuyến", "Mã hệ thống tuyến"],
            $"bao-cao-ty-le-lap-day-{range.FromDate:yyyyMMdd}-{range.ToDate:yyyyMMdd}.xlsx",
            PercentageColumns: new HashSet<int> { 7 },
            Title: "Báo cáo tỷ lệ lấp đầy",
            ReportPeriod: $"{range.FromDate:dd/MM/yyyy} - {range.ToDate:dd/MM/yyyy}",
            ExportedAt: _clock.UtcNow);
        return _writer.WriteAsync(spec, ToRowsAsync(request.OperatorId, range, ct), ct);
    }

    private async IAsyncEnumerable<ExcelReportRow> ToRowsAsync(
        Guid operatorId,
        OperatorReportRange range,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in _trips
            .StreamOperatorOccupancyRowsAsync(operatorId, range.FromUtc, range.ToUtc, ct)
            .WithCancellation(ct)
            .ConfigureAwait(false))
        {
            var occupancy = row.SellableSeatCount == 0
                ? 0m
                : Math.Round(row.BookedSeatCount * 100m / row.SellableSeatCount, 2, MidpointRounding.AwayFromZero);
            yield return new ExcelReportRow([
                ExcelReportCell.TextValue(row.TripCode),
                ExcelReportCell.TextValue(row.RouteName),
                ExcelReportCell.TextValue(row.VehicleLicensePlate),
                ExcelReportCell.TextValue(TripReportLabels.Status(row.Status)),
                ExcelReportCell.DateTimeValue(row.DepartureAt),
                ExcelReportCell.IntegerValue(row.SellableSeatCount),
                ExcelReportCell.IntegerValue(row.BookedSeatCount),
                ExcelReportCell.DecimalValue(occupancy),
                ExcelReportCell.TextValue(row.TripId.ToString("D")),
                ExcelReportCell.TextValue(row.RouteId.ToString("D")),
            ]);
        }
    }
}
