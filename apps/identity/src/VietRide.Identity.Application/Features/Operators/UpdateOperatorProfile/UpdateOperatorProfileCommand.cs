using System.Text.Json;
using MediatR;

namespace VietRide.Identity.Application.Features.Operators;

public sealed record UpdateOperatorProfileCommand(
    Guid OperatorId,
    string CallerRole,
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
    JsonElement? LuggagePolicy) : IRequest<OperatorProfileResponse>;
