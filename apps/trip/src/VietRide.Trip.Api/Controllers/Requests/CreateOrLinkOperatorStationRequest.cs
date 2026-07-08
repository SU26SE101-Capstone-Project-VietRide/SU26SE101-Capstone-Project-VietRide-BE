using System.Text.Json;

namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record CreateOrLinkOperatorStationRequest(
    Guid? StationId,
    string? DisplayNameOverride,
    string? CounterLocation,
    string? ContactPhone,
    string? Instructions,
    string? Name,
    string? City,
    string? Province,
    decimal? Latitude,
    decimal? Longitude,
    string? AddressStreet,
    string? ContactEmail,
    JsonElement? OperatingHours,
    JsonElement? Facilities,
    bool SupportsShuttle,
    Guid? LocationId = null,
    string? LocationCode = null);
