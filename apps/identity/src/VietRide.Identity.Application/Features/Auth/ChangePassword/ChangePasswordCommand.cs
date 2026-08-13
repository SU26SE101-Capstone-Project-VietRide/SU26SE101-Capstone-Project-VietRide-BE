using MediatR;

namespace VietRide.Identity.Application.Features.Auth.ChangePassword;

public sealed record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword,
    string? IpAddress,
    string? UserAgent) : IRequest<ChangePasswordResponseDto>;
