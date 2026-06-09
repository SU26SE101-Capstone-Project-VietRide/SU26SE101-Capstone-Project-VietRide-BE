using MediatR;

namespace VietRide.Trip.Application.Features.Stops;

public sealed record CreateStopCommand(
    Guid OperatorId,
    string? Name,
    decimal? Latitude,
    decimal? Longitude,
    string? Description,
    string? Address,
    string? GooglePlaceId) : IRequest<StopDto>;
