using MediatR;

namespace VietRide.Identity.Application.Features.Firebase.CreateFirebaseCustomToken;

public sealed record CreateFirebaseCustomTokenCommand(
    Guid UserId,
    string CallerRole,
    Guid? ClaimedOperatorId) : IRequest<FirebaseCustomTokenResponse>;
