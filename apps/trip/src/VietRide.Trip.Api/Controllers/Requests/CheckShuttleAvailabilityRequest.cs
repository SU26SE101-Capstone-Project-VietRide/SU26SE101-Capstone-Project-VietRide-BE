namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record CheckShuttleAvailabilityRequest(
    Guid MainTripId,
    string Direction,
    Guid DriverUserId,
    Guid VehicleId,
    DateTimeOffset ScheduledDepartureTime,
    DateTimeOffset ScheduledEndTime,
    IReadOnlyList<Guid> OrderedBookingIds);
