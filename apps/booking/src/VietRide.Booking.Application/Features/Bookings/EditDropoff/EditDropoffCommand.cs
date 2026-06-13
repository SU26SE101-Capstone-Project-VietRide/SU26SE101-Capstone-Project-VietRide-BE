using MediatR;

namespace VietRide.Booking.Application.Features.Bookings.EditDropoff;

/// <summary>
/// Command for POST /v1/bookings/{bookingId}/edit-dropoff.
/// v1 dropoff edits are price-neutral and do not call payment/refund seams.
/// </summary>
public sealed record EditDropoffCommand(
    Guid BookingId,
    Guid PassengerUserId,
    string IdempotencyKey,
    Guid? DropoffStationId,
    Guid? DropoffStopId) : IRequest<EditDropoffResult>;
