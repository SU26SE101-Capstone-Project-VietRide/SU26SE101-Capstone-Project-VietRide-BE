using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.OperatorReports;

public sealed record BookingOperatorReportRow(
    Guid BookingId,
    string BookingCode,
    Guid TripId,
    string? RouteName,
    string? OriginName,
    string? DestinationName,
    BookingStatus Status,
    long PassengerCount,
    long TotalAmountVnd,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    BookingCancellationReason? CancellationReason);
