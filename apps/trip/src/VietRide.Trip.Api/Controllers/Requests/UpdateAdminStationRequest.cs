using System.Text.Json;

namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record UpdateAdminStationRequest(
    string? Name,
    string? AddressStreet,
    Guid? LocationId,
    string? City,
    string? Ward,
    decimal? Latitude,
    decimal? Longitude,
    string? ContactPhone,
    string? ContactEmail,
    JsonElement? OperatingHours,
    JsonElement? Facilities,
    bool? SupportsShuttle,
    bool? IsActive);
