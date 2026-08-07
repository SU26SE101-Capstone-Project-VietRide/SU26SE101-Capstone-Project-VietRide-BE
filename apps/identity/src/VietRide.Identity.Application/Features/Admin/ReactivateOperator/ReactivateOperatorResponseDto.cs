namespace VietRide.Identity.Application.Features.Admin.ReactivateOperator;

public sealed record ReactivateOperatorResponseDto(
    Guid OperatorId,
    string RegistrationStatus,
    bool IsActive);
