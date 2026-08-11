using System.Text.Json;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

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
    JsonElement LuggagePolicy,
    DateTimeOffset? SuspendedAt = null,
    string? SuspendReason = null)
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
                OperatorProfilePolicyValidator.DefaultLuggagePolicy()),
            operatorProfile.RegistrationStatus == OperatorRegistrationStatus.SUSPENDED
                ? operatorProfile.SuspendedAt
                : null,
            operatorProfile.RegistrationStatus == OperatorRegistrationStatus.SUSPENDED
                ? operatorProfile.SuspendReason
                : null);
    }
}
