namespace VietRide.Identity.Application.Features.Admin.LockUser;

public sealed record LockUserResponseDto(Guid UserId, string Status, bool StatusChanged);
