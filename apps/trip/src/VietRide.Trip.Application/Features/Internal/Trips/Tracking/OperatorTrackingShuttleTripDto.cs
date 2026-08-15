namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed record OperatorTrackingShuttleTripDto(
    Guid ShuttleTripId,
    Guid MainTripId,
    string Status);
