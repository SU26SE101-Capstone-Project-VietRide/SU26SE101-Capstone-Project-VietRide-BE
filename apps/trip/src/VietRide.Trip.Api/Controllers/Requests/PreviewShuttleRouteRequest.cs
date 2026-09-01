namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record PreviewShuttleRouteRequest(
    Guid MainTripId,
    string Direction,
    DateTimeOffset ScheduledDepartureTime,
    IReadOnlyList<Guid> OrderedBookingIds);
