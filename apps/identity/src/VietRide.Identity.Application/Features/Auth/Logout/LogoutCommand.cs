using MediatR;

namespace VietRide.Identity.Application.Features.Auth.Logout;

/// <summary>Command for revoking a refresh token on explicit logout.</summary>
public sealed record LogoutCommand(string RefreshToken) : IRequest<Unit>;
