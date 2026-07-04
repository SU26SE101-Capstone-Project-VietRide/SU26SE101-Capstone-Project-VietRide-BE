using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.Cargo;

public sealed record CargoMutationCommand(
    Guid TripId,
    Guid ParcelId,
    decimal WeightKg,
    string Action) : IRequest<CargoCapacityDto>;
