using MediatR;

namespace VietRide.Identity.Application.Features.Users.CompleteProfile;

/// <summary>Command for POST /v1/users/me/complete-profile.</summary>
public sealed record CompleteProfileCommand(
    Guid UserId,
    string? Phone) : IRequest<CompleteProfileResponseDto>;
