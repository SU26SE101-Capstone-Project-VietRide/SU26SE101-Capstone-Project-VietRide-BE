using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed record GetTripRouteStopsTrackingQuery(Guid TripId)
    : IRequest<TripRouteStopsTrackingResponse>;
