using MediatR;

namespace VietRide.Trip.Application.Features.Stations.MergeStations;

public sealed record MergeStationsCommand(
    Guid PrimaryStationId,
    Guid DuplicateStationId,
    Guid ActorUserId,
    string? IpAddress,
    string? UserAgent) : IRequest<MergeStationsResponse>;
