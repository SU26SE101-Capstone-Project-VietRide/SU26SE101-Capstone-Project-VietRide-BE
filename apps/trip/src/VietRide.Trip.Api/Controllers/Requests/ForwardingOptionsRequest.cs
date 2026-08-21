namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record ForwardingOptionsRequest(
    Guid OperatorId,
    Guid? ExcludedTripId,
    string PickupLocationType,
    Guid PickupLocationId,
    string TargetLocationType,
    Guid TargetLocationId,
    decimal WeightKg,
    decimal VolumeM3,
    DateTimeOffset EarliestDeparture,
    int Limit = 20);
