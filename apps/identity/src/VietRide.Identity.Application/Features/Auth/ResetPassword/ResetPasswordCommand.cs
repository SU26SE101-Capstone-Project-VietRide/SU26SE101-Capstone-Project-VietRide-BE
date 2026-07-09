using MediatR;

namespace VietRide.Identity.Application.Features.Auth.ResetPassword;

public sealed record ResetPasswordCommand(
    string Email,
    string Code,
    string NewPassword) : IRequest<ResetPasswordResponseDto>;
