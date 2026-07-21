using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.Internal.Trips.Cargo;

[SkipTransaction]
public sealed record CargoMutationCommand(
    Guid TripId,
    Guid ParcelId,
    decimal WeightKg,
    decimal VolumeM3,
    bool AllowCapacityOverflow,
    string Action) : IRequest<CargoCapacityDto>;
