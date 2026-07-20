namespace VietRide.Booking.Application.Features.OperatorReports;

public sealed record BookingOperatorReportRow(
    Guid BookingId,
    string BookingCode,
    Guid TripId,
    string Status,
    long PassengerCount,
    long TotalAmountVnd,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason);
