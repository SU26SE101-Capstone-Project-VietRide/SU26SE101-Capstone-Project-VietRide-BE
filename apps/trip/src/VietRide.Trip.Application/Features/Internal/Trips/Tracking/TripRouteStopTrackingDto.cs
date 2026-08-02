namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed record TripRouteStopTrackingDto(
    Guid StopId,
    double Latitude,
    double Longitude,
    int Sequence,
    IReadOnlyList<Guid>? AlertRecipientUserIds,
    DateTimeOffset? EstimatedArrivalTime,
    string? Status = null);
