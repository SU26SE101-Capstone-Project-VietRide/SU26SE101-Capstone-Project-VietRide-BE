using MediatR;

namespace VietRide.Identity.Application.Features.Auth.ForgotPassword;

public sealed record ForgotPasswordCommand(string Email) : IRequest<ForgotPasswordResponseDto>;
