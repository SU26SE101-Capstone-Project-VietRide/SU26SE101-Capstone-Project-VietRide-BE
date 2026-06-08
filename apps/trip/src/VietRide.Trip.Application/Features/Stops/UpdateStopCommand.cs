using MediatR;

namespace VietRide.Trip.Application.Features.Stops;

public sealed record UpdateStopCommand(
    Guid OperatorId,
    Guid StopId,
    string? Name,
    decimal? Latitude,
    decimal? Longitude,
    string? Description,
    string? Address,
    string? GooglePlaceId) : IRequest<StopDto>;
