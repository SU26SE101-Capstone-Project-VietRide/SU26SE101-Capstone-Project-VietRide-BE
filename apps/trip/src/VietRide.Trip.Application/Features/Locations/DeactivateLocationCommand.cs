using MediatR;

namespace VietRide.Trip.Application.Features.Locations;

public sealed record DeactivateLocationCommand(Guid Id) : IRequest<LocationDto>;
