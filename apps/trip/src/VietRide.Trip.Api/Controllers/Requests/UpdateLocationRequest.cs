namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record UpdateLocationRequest(
    string? Code,
    string? Name,
    string? Type,
    int? SortOrder,
    bool? IsActive);
