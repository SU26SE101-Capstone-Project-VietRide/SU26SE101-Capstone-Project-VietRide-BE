namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record CreateLocationRequest(
    string? Code,
    string? Name,
    string? Type,
    int? SortOrder,
    bool? IsActive);
