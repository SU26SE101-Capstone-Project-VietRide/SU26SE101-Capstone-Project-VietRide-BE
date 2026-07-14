namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record UpdateOperatorStationRequest(
    string? DisplayNameOverride,
    string? CounterLocation,
    string? ContactPhone,
    string? Instructions);
