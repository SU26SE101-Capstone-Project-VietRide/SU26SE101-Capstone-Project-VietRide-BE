using MediatR;

namespace VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;

public sealed record GetAssignedTripRouteQuery(Guid TripId, Guid UserId)
    : IRequest<DriverTripRouteDto>;
