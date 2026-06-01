using MediatR;
using VietRide.Identity.Application.Features.Auth.Login;

namespace VietRide.Identity.Application.Features.Auth.Refresh;

/// <summary>Command for rotating a refresh token.</summary>
public sealed record RefreshCommand(string RefreshToken) : IRequest<TokenBundleDto>;
