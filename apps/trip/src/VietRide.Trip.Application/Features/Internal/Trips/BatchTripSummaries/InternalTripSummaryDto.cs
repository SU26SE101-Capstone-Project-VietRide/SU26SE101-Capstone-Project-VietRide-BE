namespace VietRide.Trip.Application.Features.Internal.Trips.BatchTripSummaries;

public sealed record InternalTripSummaryDto(
    Guid TripId,
    string Status,
    DateTimeOffset DepartureAt,
    DateTimeOffset ArrivalEstimate,
    InternalTripRouteSummaryDto Route,
    InternalTripVehicleSummaryDto Vehicle,
    Guid DriverUserId,
    Guid? AssistantUserId);
