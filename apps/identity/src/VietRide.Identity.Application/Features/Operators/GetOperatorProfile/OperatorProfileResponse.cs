using System.Text.Json;
using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Application.Features.Operators;

public sealed record OperatorProfileResponse(
    Guid OperatorId,
    string Name,
    string BusinessRegistrationNumber,
    string TaxCode,
    string ContactEmail,
    string ContactPhone,
    string? LogoUrl,
    OperatorProfileAddressResponse Address,
    string? RepresentativeName,
    string? RepresentativePhone,
    string RegistrationStatus,
    bool IsActive,
    JsonElement? CancellationPolicy,
    JsonElement ParcelNoShowPolicy,
    JsonElement LuggagePolicy)
{
    public static OperatorProfileResponse FromOperator(Operator operatorProfile)
    {
        ArgumentNullException.ThrowIfNull(operatorProfile);

        return new OperatorProfileResponse(
            operatorProfile.Id,
            operatorProfile.Name,
            operatorProfile.BusinessRegistrationNumber,
            operatorProfile.TaxCode,
            operatorProfile.ContactEmail,
            operatorProfile.ContactPhone,
            operatorProfile.LogoUrl,
            new OperatorProfileAddressResponse(
                operatorProfile.AddressStreet,
                operatorProfile.AddressWard,
                operatorProfile.AddressDistrict,
                operatorProfile.AddressProvince),
            operatorProfile.RepresentativeName,
            operatorProfile.RepresentativePhone,
            operatorProfile.RegistrationStatus.ToString(),
            operatorProfile.IsActive,
            OperatorProfilePolicyValidator.ToNullableJsonElement(operatorProfile.CancellationPolicy),
            OperatorProfilePolicyValidator.ToJsonElement(
                operatorProfile.ParcelNoShowPolicy,
                OperatorProfilePolicyValidator.DefaultParcelNoShowPolicy()),
            OperatorProfilePolicyValidator.ToJsonElement(
                operatorProfile.LuggagePolicy,
                OperatorProfilePolicyValidator.DefaultLuggagePolicy()));
    }
}
