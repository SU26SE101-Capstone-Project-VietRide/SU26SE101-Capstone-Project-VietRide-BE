using MediatR;

namespace VietRide.Identity.Application.Features.Auth.GetJwks;

/// <summary>Query that returns the JWKS JSON for GET /v1/.well-known/jwks.json.</summary>
public sealed record GetJwksQuery : IRequest<string>;
