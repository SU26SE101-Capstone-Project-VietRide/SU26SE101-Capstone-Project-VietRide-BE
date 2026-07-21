using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Firebase.CreateFirebaseCustomToken;

public sealed class CreateFirebaseCustomTokenCommandHandler
    : IRequestHandler<CreateFirebaseCustomTokenCommand, FirebaseCustomTokenResponse>
{
    private readonly IUserRepository _users;
    private readonly IOperatorRepository _operators;
    private readonly IFirebaseAuthService _firebaseAuth;

    public CreateFirebaseCustomTokenCommandHandler(
        IUserRepository users,
        IOperatorRepository operators,
        IFirebaseAuthService firebaseAuth)
    {
        _users = users;
        _operators = operators;
        _firebaseAuth = firebaseAuth;
    }

    public async Task<FirebaseCustomTokenResponse> Handle(
        CreateFirebaseCustomTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.OPERATOR_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only OPERATOR_ADMIN can request a Firebase custom token.");

        var user = await _users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new UnauthorizedException("UNAUTHORIZED", "The authenticated user no longer exists.");

        if (user.Role != UserRole.OPERATOR_ADMIN
            || user.Status != UserStatus.ACTIVE
            || user.OperatorId is null
            || request.ClaimedOperatorId != user.OperatorId)
        {
            throw new ForbiddenException("FORBIDDEN", "The authenticated user is not eligible for vehicle image upload.");
        }

        var operatorEntity = await _operators.GetByIdNoTrackingAsync(user.OperatorId.Value, cancellationToken);
        if (operatorEntity is null
            || operatorEntity.RegistrationStatus != OperatorRegistrationStatus.APPROVED
            || !operatorEntity.IsActive)
        {
            throw new ForbiddenException("FORBIDDEN", "The operator is not active and approved.");
        }

        var token = await _firebaseAuth.CreateOperatorCustomTokenAsync(
            user.Id,
            user.OperatorId.Value,
            cancellationToken);

        return new FirebaseCustomTokenResponse(token);
    }
}
