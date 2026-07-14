using MediatR;

namespace VietRide.Trip.Application.Features.Stops;

public sealed record UpdateAdminStopCommand(Guid StopId, string? Name, decimal? Latitude, decimal? Longitude,
    string? Description, string? Address, string? GooglePlaceId, bool? IsActive) : IRequest<StopDto>;
