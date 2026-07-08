using MediatR;

namespace VietRide.Trip.Application.Features.Stops;

public sealed record CreateStopCommand(
    Guid OperatorId,
    string? Name,
    decimal? Latitude,
    decimal? Longitude,
    string? Description,
    string? Address,
    string? GooglePlaceId,
    Guid? LocationId = null,
    string? LocationCode = null) : IRequest<StopDto>;
