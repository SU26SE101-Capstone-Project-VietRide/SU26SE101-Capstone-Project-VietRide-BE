namespace VietRide.Identity.Application.Features.Admin.CreateOperator;

public sealed record OperatorAdminSummaryDto(
    Guid UserId,
    string Email,
    string Phone,
    string DisplayName,
    string Role,
    string Status);
