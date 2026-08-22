namespace VietRide.Trip.Api.Controllers.Requests;

public sealed class ReassignShuttleTripRequest
{
    public Guid? DriverUserId { get; init; }
    public Guid? VehicleId { get; init; }
    public string? Reason { get; init; }
}
