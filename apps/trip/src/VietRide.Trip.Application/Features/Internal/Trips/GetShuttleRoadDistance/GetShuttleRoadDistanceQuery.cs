using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.GetShuttleRoadDistance;

public sealed record GetShuttleRoadDistanceQuery(
    Guid TripId,
    string Direction,
    decimal Latitude,
    decimal Longitude) : IRequest<ShuttleRoadDistanceDto>;

public sealed record ShuttleRoadDistanceDto(int DistanceMeters);
