using MediatR;

namespace VietRide.Trip.Application.Features.Locations;

public sealed record ListLocationsQuery : IRequest<IReadOnlyList<LocationDto>>;
