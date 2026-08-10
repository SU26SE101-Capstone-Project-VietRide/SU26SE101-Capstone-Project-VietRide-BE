using System.Text.Json;
using VietRide.Identity.Application.Features.Operators;
using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Application.Features.Admin.GetOperatorDetail;

public sealed record AdminOperatorDetailDto(
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
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RejectedAt,
    string? RejectReason,
    DateTimeOffset? SuspendedAt,
    string? SuspendReason,
    JsonElement? CancellationPolicy,
    JsonElement ParcelNoShowPolicy,
    JsonElement LuggagePolicy)
{
    public static AdminOperatorDetailDto FromOperator(Operator operatorEntity)
    {
        ArgumentNullException.ThrowIfNull(operatorEntity);

        return new AdminOperatorDetailDto(
            operatorEntity.Id,
            operatorEntity.Name,
            operatorEntity.BusinessRegistrationNumber,
            operatorEntity.TaxCode,
            operatorEntity.ContactEmail,
            operatorEntity.ContactPhone,
            operatorEntity.LogoUrl,
            new OperatorProfileAddressResponse(
                operatorEntity.AddressStreet,
                operatorEntity.AddressWard,
                operatorEntity.AddressProvince),
            operatorEntity.RepresentativeName,
            operatorEntity.RepresentativePhone,
            operatorEntity.RegistrationStatus.ToString(),
            operatorEntity.IsActive,
            operatorEntity.CreatedAt,
            operatorEntity.UpdatedAt,
            operatorEntity.ApprovedAt,
            operatorEntity.RejectedAt,
            operatorEntity.RejectReason,
            operatorEntity.SuspendedAt,
            operatorEntity.SuspendReason,
            OperatorProfilePolicyValidator.ToNullableJsonElement(operatorEntity.CancellationPolicy),
            OperatorProfilePolicyValidator.ToJsonElement(
                operatorEntity.ParcelNoShowPolicy,
                OperatorProfilePolicyValidator.DefaultParcelNoShowPolicy()),
            OperatorProfilePolicyValidator.ToJsonElement(
                operatorEntity.LuggagePolicy,
                OperatorProfilePolicyValidator.DefaultLuggagePolicy()));
    }
}
