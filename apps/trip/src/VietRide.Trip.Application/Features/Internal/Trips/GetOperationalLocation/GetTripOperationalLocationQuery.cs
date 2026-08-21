using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.GetOperationalLocation;

public sealed record GetTripOperationalLocationQuery(Guid TripId)
    : IRequest<TripOperationalLocationDto>;
