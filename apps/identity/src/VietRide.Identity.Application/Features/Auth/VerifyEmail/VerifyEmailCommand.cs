using MediatR;

namespace VietRide.Identity.Application.Features.Auth.VerifyEmail;

/// <summary>Command for verifying an email OTP code.</summary>
public sealed record VerifyEmailCommand(
    string Email,
    string Code,
    string Purpose) : IRequest<VerifyEmailResponseDto>;
