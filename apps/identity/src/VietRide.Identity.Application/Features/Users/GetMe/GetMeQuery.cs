using VietRide.Shared.Application.Cqrs;

namespace VietRide.Identity.Application.Features.Users.GetMe;

/// <summary>Query for GET /v1/users/me.</summary>
public sealed record GetMeQuery(Guid UserId) : IQuery<GetMeResponseDto>;
