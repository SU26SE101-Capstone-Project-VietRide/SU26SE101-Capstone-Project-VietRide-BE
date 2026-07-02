namespace VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperator;

public sealed record InternalOperatorLookupDto(
    Guid OperatorId,
    string Name,
    string RegistrationStatus,
    bool IsActive,
    string ContactEmail,
    string ContactPhone,
    string BusinessRegistrationNumber,
    string TaxCode,
    InternalParcelNoShowPolicyDto? ParcelNoShowPolicy);

public sealed record InternalParcelNoShowPolicyDto(
    decimal NoShowFeePercent,
    int AdditionalPaymentTimeoutMinutes);
