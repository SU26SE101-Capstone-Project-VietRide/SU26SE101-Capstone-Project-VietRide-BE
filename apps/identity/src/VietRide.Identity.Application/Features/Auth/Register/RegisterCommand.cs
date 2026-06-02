using MediatR;

namespace VietRide.Identity.Application.Features.Auth.Register;

/// <summary>Command for PASSENGER self-registration via email + password.</summary>
public sealed record RegisterCommand(
    string Email,
    string Password,
    string DisplayName,
    string Phone) : IRequest<RegisterResponseDto>;
