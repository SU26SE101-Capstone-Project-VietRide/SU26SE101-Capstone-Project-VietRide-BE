using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.ForwardingOptions;

public sealed record GetForwardingOptionsQuery(
    Guid OperatorId,
    Guid? ExcludedTripId,
    string PickupLocationType,
    Guid PickupLocationId,
    string TargetLocationType,
    Guid TargetLocationId,
    decimal WeightKg,
    decimal VolumeM3,
    DateTimeOffset EarliestDeparture,
    int Limit) : IRequest<IReadOnlyList<InternalForwardingOptionDto>>;
