namespace VietRide.Booking.Application.Events;

public sealed record BookingDisruptedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid BookingId,
    string BookingCode,
    Guid TripId,
    Guid OperatorId,
    Guid UserId,
    decimal TraveledRatio,
    long RefundAmount,
    string CancellationReason)
{
    public const string EventTypeValue = "booking.booking.disrupted";
}
