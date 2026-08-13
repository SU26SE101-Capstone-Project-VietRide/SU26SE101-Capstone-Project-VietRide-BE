namespace VietRide.Identity.Application.Features.OperatorUsers.LockOperatorUser;

public sealed record LockOperatorUserResponseDto(
    Guid UserId,
    string Status,
    bool StatusChanged);
