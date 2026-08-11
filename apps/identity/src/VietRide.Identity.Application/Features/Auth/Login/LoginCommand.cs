using MediatR;

namespace VietRide.Identity.Application.Features.Auth.Login;

/// <summary>Command for authenticating a user with email and password.</summary>
public sealed record LoginCommand(
    string Email,
    string Password,
    string ClientKind = "UNKNOWN") : IRequest<TokenBundleDto>;
