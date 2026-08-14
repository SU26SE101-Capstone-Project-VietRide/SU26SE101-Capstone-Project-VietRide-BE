namespace VietRide.Identity.Application.Features.InternalUsers.SearchInternalUsers;

public sealed record SearchInternalUsersResponseDto(IReadOnlyList<Guid> UserIds);
