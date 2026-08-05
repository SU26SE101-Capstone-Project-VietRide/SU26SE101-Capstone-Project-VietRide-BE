namespace VietRide.Trip.Api.Controllers.Requests;

public sealed class CreateShuttleTripRequest
{
    public Guid MainTripId { get; init; }
    public string? Direction { get; init; }
    public Guid DriverUserId { get; init; }
    public Guid VehicleId { get; init; }
    public DateTimeOffset ScheduledDepartureTime { get; init; }
    public DateTimeOffset ScheduledEndTime { get; init; }
    public IReadOnlyList<Guid> OrderedBookingIds { get; init; } = [];
    public string? Notes { get; init; }
}
