using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed record GetTripRouteGeometryTrackingQuery(Guid TripId)
    : IRequest<TripRouteGeometryTrackingResponse>;
