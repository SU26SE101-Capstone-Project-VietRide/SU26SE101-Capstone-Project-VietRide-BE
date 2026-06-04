using MediatR;

namespace VietRide.Identity.Application.Features.Admin.CreateAdminUser;

/// <summary>Command for POST /v1/admin/users.</summary>
public sealed record CreateAdminUserCommand(
    Guid CallerUserId,
    string CallerRole,
    string Email,
    string DisplayName,
    string Role) : IRequest<CreateAdminUserResponseDto>;
