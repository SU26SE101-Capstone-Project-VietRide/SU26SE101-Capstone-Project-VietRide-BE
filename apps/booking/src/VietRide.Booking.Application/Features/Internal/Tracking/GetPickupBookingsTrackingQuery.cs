using MediatR;

namespace VietRide.Booking.Application.Features.Internal.Tracking;

public sealed record GetPickupBookingsTrackingQuery(Guid TripId, Guid StopId)
    : IRequest<PickupBookingsTrackingResponse>;
