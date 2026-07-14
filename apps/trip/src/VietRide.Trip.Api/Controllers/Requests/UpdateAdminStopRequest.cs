namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record UpdateAdminStopRequest(
    string? Name,
    decimal? Latitude,
    decimal? Longitude,
    string? Description,
    string? Address,
    string? GooglePlaceId,
    bool? IsActive);
