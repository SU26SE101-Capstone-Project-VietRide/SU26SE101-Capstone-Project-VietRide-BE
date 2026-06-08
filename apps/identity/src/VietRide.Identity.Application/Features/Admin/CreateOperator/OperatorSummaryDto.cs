namespace VietRide.Identity.Application.Features.Admin.CreateOperator;

public sealed record OperatorSummaryDto(
    Guid OperatorId,
    string Name,
    string RegistrationStatus,
    string ContactEmail,
    string ContactPhone,
    string BusinessRegistrationNumber,
    string TaxCode);
