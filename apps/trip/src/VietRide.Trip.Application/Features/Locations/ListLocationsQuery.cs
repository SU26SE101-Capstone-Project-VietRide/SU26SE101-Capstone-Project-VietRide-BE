using MediatR;

namespace VietRide.Trip.Application.Features.Locations;

public sealed record ListLocationsQuery(
    string? ParentCode,
    string? Search) : IRequest<IReadOnlyList<LocationDto>>;
