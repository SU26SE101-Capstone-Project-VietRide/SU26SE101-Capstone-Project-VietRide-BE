namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record CreateStopRequest(
    string? Name,
    decimal? Latitude,
    decimal? Longitude,
    string? Description,
    string? Address,
    string? GooglePlaceId,
    Guid? LocationId = null,
    string? LocationCode = null);
