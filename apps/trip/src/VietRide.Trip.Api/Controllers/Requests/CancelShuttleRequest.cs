namespace VietRide.Trip.Api.Controllers.Requests;

public sealed class CancelShuttleRequest
{
    public string Reason { get; init; } = string.Empty;
}
