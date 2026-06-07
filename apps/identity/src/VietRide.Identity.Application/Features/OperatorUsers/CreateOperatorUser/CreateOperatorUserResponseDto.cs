namespace VietRide.Identity.Application.Features.OperatorUsers.CreateOperatorUser;

public sealed record CreateOperatorUserResponseDto(
    Guid UserId,
    string Email,
    string Phone,
    string DisplayName,
    string Role,
    string Status,
    Guid OperatorId,
    DateTimeOffset InitialPasswordExpiresAt);
