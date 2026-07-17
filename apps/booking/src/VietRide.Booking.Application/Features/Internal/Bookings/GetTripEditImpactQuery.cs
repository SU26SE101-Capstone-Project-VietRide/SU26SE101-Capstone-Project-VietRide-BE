using VietRide.Shared.Application.Cqrs;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed record GetTripEditImpactQuery(Guid TripId, Guid OperatorId)
    : IQuery<TripEditImpactDto>;
