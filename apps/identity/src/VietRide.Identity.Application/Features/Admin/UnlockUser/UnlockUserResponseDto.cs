namespace VietRide.Identity.Application.Features.Admin.UnlockUser;

public sealed record UnlockUserResponseDto(Guid UserId, string Status, bool StatusChanged);
