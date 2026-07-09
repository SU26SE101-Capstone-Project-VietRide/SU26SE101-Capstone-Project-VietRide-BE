using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.Cargo;

public sealed record CargoMutationCommand(
    Guid TripId,
    Guid ParcelId,
    decimal WeightKg,
    decimal VolumeM3,
    bool AllowCapacityOverflow,
    string Action) : IRequest<CargoCapacityDto>;
