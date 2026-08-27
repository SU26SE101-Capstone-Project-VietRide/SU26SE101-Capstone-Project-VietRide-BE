using VietRide.Parcel.Application.Features.Reliability.ReadModels;

namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed record AssistantOperationalLocationResponse(
    ReliabilityLocationResponse Location,
    string Status,
    DateTimeOffset? ActualArrivalAt,
    DateTimeOffset? ActualDepartureAt);
