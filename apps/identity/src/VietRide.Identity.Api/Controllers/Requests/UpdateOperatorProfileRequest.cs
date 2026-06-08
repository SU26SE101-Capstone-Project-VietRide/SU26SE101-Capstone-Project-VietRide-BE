using System.Text.Json;

namespace VietRide.Identity.Api.Controllers.Requests;

public sealed record UpdateOperatorProfileRequest(
    string Name,
    string ContactPhone,
    string? LogoUrl,
    string AddressStreet,
    string AddressWard,
    string AddressDistrict,
    string AddressProvince,
    string RepresentativeName,
    string RepresentativePhone,
    JsonElement? CancellationPolicy,
    JsonElement? ParcelNoShowPolicy,
    JsonElement? LuggagePolicy);
