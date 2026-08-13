namespace VietRide.Identity.Application.Features.OperatorUsers.UnlockOperatorUser;

public sealed record UnlockOperatorUserResponseDto(
    Guid UserId,
    string Status,
    bool StatusChanged);
