using MediatR;

namespace VietRide.Booking.Application.Features.Bookings.EditPickup;

/// <summary>
/// Command for POST /v1/bookings/{bookingId}/edit-pickup.
/// The edit is price-neutral-only: any fare difference is rejected.
/// </summary>
public sealed record EditPickupCommand(
    Guid BookingId,
    Guid PassengerUserId,
    string IdempotencyKey,
    Guid? PickupStationId,
    Guid? PickupStopId,
    string PaymentMethod) : IRequest<EditPickupResult>;
