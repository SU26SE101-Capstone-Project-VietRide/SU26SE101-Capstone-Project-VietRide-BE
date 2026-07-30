using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.Internal.Trips.Cargo;

[SkipTransaction]
public sealed record TransferCargoCommand(
    Guid SourceTripId,
    Guid ParcelId,
    Guid TargetTripId,
    string TargetState,
    bool AllowCapacityOverflow) : IRequest<CargoTransferDto>;
