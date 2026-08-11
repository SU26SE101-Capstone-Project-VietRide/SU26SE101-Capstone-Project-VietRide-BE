using MediatR;

namespace VietRide.Trip.Application.Features.Locations;

public sealed record ListLocationsQuery(
    string? ParentCode,
    string? Search,
    string? Type = null) : IRequest<IReadOnlyList<LocationDto>>;
