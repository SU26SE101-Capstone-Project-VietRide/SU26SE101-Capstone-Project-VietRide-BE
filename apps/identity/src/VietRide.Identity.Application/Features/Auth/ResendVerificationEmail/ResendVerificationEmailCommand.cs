using MediatR;

namespace VietRide.Identity.Application.Features.Auth.ResendVerificationEmail;

/// <summary>Command for resending an email verification OTP.</summary>
public sealed record ResendVerificationEmailCommand(
    string Email,
    string Purpose) : IRequest<ResendVerificationEmailResponseDto>;
