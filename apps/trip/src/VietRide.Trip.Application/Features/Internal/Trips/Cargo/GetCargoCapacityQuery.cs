using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.Cargo;

public sealed record GetCargoCapacityQuery(Guid TripId, Guid? OperatorId) : IRequest<CargoCapacityDto>;
