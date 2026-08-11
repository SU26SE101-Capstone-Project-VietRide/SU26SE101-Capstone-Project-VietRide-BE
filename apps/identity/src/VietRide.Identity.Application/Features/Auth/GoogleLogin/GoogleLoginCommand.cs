using MediatR;
using VietRide.Identity.Application.Features.Auth.Login;

namespace VietRide.Identity.Application.Features.Auth.GoogleLogin;

public sealed record GoogleLoginCommand(
    string IdToken,
    string ClientKind = "UNKNOWN") : IRequest<TokenBundleDto>;
