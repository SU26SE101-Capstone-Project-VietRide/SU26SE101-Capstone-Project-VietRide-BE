namespace VietRide.Parcel.Application.Features.Reliability.ReadModels;

public sealed record ReliabilityCustodySummaryResponse(
    string LastEventType,
    ReliabilityLocationResponse LastConfirmedLocation,
    DateTimeOffset LastConfirmedAt,
    Guid? CurrentTripId,
    Guid? CurrentVehicleId,
    string TrackingConfidence,
    bool HasTrackingGap);
