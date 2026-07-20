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
        var prefix = isCancellation ? "cancellation" : "bookings";
        IReadOnlyList<string> headers = isCancellation
            ? ["booking_id", "booking_code", "trip_id", "status", "cancelled_at", "cancellation_reason", "total_amount_vnd"]
            : ["booking_id", "booking_code", "trip_id", "status", "passenger_count", "total_amount_vnd", "created_at", "confirmed_at", "completed_at"];
        var currencyColumns = isCancellation
            ? (IReadOnlySet<int>)new HashSet<int> { 6 }
            : new HashSet<int> { 5 };
        var spec = new ExcelReportSpec(
            isCancellation ? "Cancellations" : "Bookings",
            headers,
            $"{prefix}-report-{range.FromDate:yyyyMMdd}-{range.ToDate:yyyyMMdd}.xlsx",
            currencyColumns);

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
                    ExcelReportCell.TextValue(row.BookingId.ToString("D")),
                    ExcelReportCell.TextValue(row.BookingCode),
                    ExcelReportCell.TextValue(row.TripId.ToString("D")),
                    ExcelReportCell.TextValue(row.Status),
                    row.CancelledAt.HasValue
                        ? ExcelReportCell.DateTimeValue(row.CancelledAt.Value.UtcDateTime)
                        : ExcelReportCell.BlankValue(),
                    ExcelReportCell.TextValue(row.CancellationReason ?? string.Empty),
                    ExcelReportCell.IntegerValue(row.TotalAmountVnd),
                ]);
            }
            else
            {
                yield return new ExcelReportRow([
                    ExcelReportCell.TextValue(row.BookingId.ToString("D")),
                    ExcelReportCell.TextValue(row.BookingCode),
                    ExcelReportCell.TextValue(row.TripId.ToString("D")),
                    ExcelReportCell.TextValue(row.Status),
                    ExcelReportCell.IntegerValue(row.PassengerCount),
                    ExcelReportCell.IntegerValue(row.TotalAmountVnd),
                    ExcelReportCell.DateTimeValue(row.CreatedAt.UtcDateTime),
                    row.ConfirmedAt.HasValue
                        ? ExcelReportCell.DateTimeValue(row.ConfirmedAt.Value.UtcDateTime)
                        : ExcelReportCell.BlankValue(),
                    row.CompletedAt.HasValue
                        ? ExcelReportCell.DateTimeValue(row.CompletedAt.Value.UtcDateTime)
                        : ExcelReportCell.BlankValue(),
                ]);
            }
        }
    }
}
