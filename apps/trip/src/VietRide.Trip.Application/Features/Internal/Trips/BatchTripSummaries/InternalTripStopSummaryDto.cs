namespace VietRide.Trip.Application.Features.Internal.Trips.BatchTripSummaries;

public sealed record InternalTripStopSummaryDto(
    Guid StopId,
    string Name,
    int OrderIndex,
    DateTimeOffset EstimatedArrivalAt,
    string Status,
    DateTimeOffset? ActualArrivalAt,
    DateTimeOffset? ActualDepartureAt);
