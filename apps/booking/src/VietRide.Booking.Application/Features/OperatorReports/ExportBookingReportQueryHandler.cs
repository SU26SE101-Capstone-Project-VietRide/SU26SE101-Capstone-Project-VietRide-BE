using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.OperatorReports;

public sealed class ExportBookingReportQueryHandler
    : IRequestHandler<ExportBookingReportQuery, ExcelReportStream>
{
    private readonly IBookingRepository _bookings;
    private readonly IExcelReportWriter _writer;
    private readonly IClock _clock;

    public ExportBookingReportQueryHandler(
        IBookingRepository bookings,
        IExcelReportWriter writer,
        IClock clock)
    {
        _bookings = bookings;
        _writer = writer;
        _clock = clock;
    }

    public Task<ExcelReportStream> Handle(
        ExportBookingReportQuery request,
        CancellationToken cancellationToken)
    {
        var range = OperatorReportRange.Create(request.From, request.To, _clock);
        var isCancellation = request.Kind == BookingOperatorReportKind.Cancellations;
        var prefix = isCancellation ? "bao-cao-huy-ve" : "bao-cao-dat-ve";
        IReadOnlyList<string> headers = isCancellation
            ? ["Mã đặt vé", "Tuyến", "Điểm đi", "Điểm đến", "Trạng thái", "Thời gian hủy", "Lý do hủy", "Tổng tiền", "Mã hệ thống đặt vé", "Mã hệ thống chuyến"]
            : ["Mã đặt vé", "Tuyến", "Điểm đi", "Điểm đến", "Trạng thái", "Số hành khách", "Tổng tiền", "Thời gian đặt", "Thời gian xác nhận", "Thời gian hoàn thành", "Mã hệ thống đặt vé", "Mã hệ thống chuyến"];
        var currencyColumns = isCancellation
            ? (IReadOnlySet<int>)new HashSet<int> { 7 }
            : new HashSet<int> { 6 };
        var title = isCancellation ? "Báo cáo hủy vé" : "Báo cáo đặt vé";
        var spec = new ExcelReportSpec(
            isCancellation ? "Hủy vé" : "Đặt vé",
            headers,
            $"{prefix}-{range.FromDate:yyyyMMdd}-{range.ToDate:yyyyMMdd}.xlsx",
            currencyColumns,
            Title: title,
            ReportPeriod: $"{range.FromDate:dd/MM/yyyy} - {range.ToDate:dd/MM/yyyy}",
            ExportedAt: _clock.UtcNow);

        return _writer.WriteAsync(
            spec,
            ToExcelRowsAsync(request.OperatorId, range, isCancellation, cancellationToken),
            cancellationToken);
    }

    private async IAsyncEnumerable<ExcelReportRow> ToExcelRowsAsync(
        Guid operatorId,
        OperatorReportRange range,
        bool cancellationOnly,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in _bookings
            .StreamOperatorReportRowsAsync(operatorId, range.FromUtc, range.ToUtc, cancellationOnly, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            if (cancellationOnly)
            {
                yield return new ExcelReportRow([
                    ExcelReportCell.TextValue(row.BookingCode),
                    ExcelReportCell.TextValue(row.RouteName ?? string.Empty),
                    ExcelReportCell.TextValue(row.OriginName ?? string.Empty),
                    ExcelReportCell.TextValue(row.DestinationName ?? string.Empty),
                    ExcelReportCell.TextValue(BookingReportLabels.Status(row.Status)),
                    row.CancelledAt.HasValue
                        ? ExcelReportCell.DateTimeValue(row.CancelledAt.Value)
                        : ExcelReportCell.BlankValue(),
                    ExcelReportCell.TextValue(BookingReportLabels.CancellationReason(row.CancellationReason)),
                    ExcelReportCell.IntegerValue(row.TotalAmountVnd),
                    ExcelReportCell.TextValue(row.BookingId.ToString("D")),
                    ExcelReportCell.TextValue(row.TripId.ToString("D")),
                ]);
            }
            else
            {
                yield return new ExcelReportRow([
                    ExcelReportCell.TextValue(row.BookingCode),
                    ExcelReportCell.TextValue(row.RouteName ?? string.Empty),
                    ExcelReportCell.TextValue(row.OriginName ?? string.Empty),
                    ExcelReportCell.TextValue(row.DestinationName ?? string.Empty),
                    ExcelReportCell.TextValue(BookingReportLabels.Status(row.Status)),
                    ExcelReportCell.IntegerValue(row.PassengerCount),
                    ExcelReportCell.IntegerValue(row.TotalAmountVnd),
                    ExcelReportCell.DateTimeValue(row.CreatedAt),
                    row.ConfirmedAt.HasValue
                        ? ExcelReportCell.DateTimeValue(row.ConfirmedAt.Value)
                        : ExcelReportCell.BlankValue(),
                    row.CompletedAt.HasValue
                        ? ExcelReportCell.DateTimeValue(row.CompletedAt.Value)
                        : ExcelReportCell.BlankValue(),
                    ExcelReportCell.TextValue(row.BookingId.ToString("D")),
                    ExcelReportCell.TextValue(row.TripId.ToString("D")),
                ]);
            }
        }
    }
}
