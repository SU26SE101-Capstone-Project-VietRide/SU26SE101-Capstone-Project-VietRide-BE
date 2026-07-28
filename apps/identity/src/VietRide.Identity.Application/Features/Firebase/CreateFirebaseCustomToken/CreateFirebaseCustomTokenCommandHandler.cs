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
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new UnauthorizedException("UNAUTHORIZED", "The authenticated user no longer exists.");

        if (user.Status != UserStatus.ACTIVE
            || !string.Equals(request.CallerRole, user.Role.ToString(), StringComparison.Ordinal)
            || request.ClaimedOperatorId != user.OperatorId)
        {
            throw new ForbiddenException("FORBIDDEN", "The authenticated user is not eligible for Firebase upload.");
        }

        var purpose = ParsePurpose(request.Purpose);
        EnsurePurposeAllowed(user.Role, purpose);
        if (purpose is FirebaseUploadPurpose.VEHICLE_IMAGE
                or FirebaseUploadPurpose.OPERATOR_LOGO
                or FirebaseUploadPurpose.PARCEL_EVIDENCE_PHOTO
                or FirebaseUploadPurpose.INCIDENT_PHOTO
            && !user.OperatorId.HasValue)
        {
            throw new ForbiddenException("FORBIDDEN", "Operator scope is required for this upload purpose.");
        }

        if (user.OperatorId.HasValue)
        {
            var operatorEntity = await _operators.GetByIdNoTrackingAsync(user.OperatorId.Value, cancellationToken);
            if (operatorEntity is null
                || operatorEntity.RegistrationStatus != OperatorRegistrationStatus.APPROVED
                || !operatorEntity.IsActive)
            {
                throw new ForbiddenException("FORBIDDEN", "The operator is not active and approved.");
            }
        }

        var purposeName = purpose.ToString();
        var token = await _firebaseAuth.CreateCustomTokenAsync(
            user.Id,
            user.Role.ToString(),
            user.OperatorId,
            purposeName,
            cancellationToken);

        return new FirebaseCustomTokenResponse(
            token,
            purposeName,
            BuildUploadPath(user.Id, user.OperatorId, purpose));
    }

    private static FirebaseUploadPurpose ParsePurpose(string? purpose)
    {
        var normalized = string.IsNullOrWhiteSpace(purpose)
            ? FirebaseUploadPurpose.VEHICLE_IMAGE.ToString()
            : purpose.Trim();
        if (!Enum.TryParse<FirebaseUploadPurpose>(normalized, ignoreCase: false, out var parsed))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "purpose must be VEHICLE_IMAGE, OPERATOR_LOGO, PARCEL_PHOTO, PARCEL_EVIDENCE_PHOTO, INCIDENT_PHOTO, or USER_AVATAR.");
        }

        return parsed;
    }

    private static void EnsurePurposeAllowed(UserRole role, FirebaseUploadPurpose purpose)
    {
        var allowed = purpose switch
        {
            FirebaseUploadPurpose.VEHICLE_IMAGE or FirebaseUploadPurpose.OPERATOR_LOGO
                => role == UserRole.OPERATOR_ADMIN,
            FirebaseUploadPurpose.PARCEL_PHOTO => role == UserRole.PASSENGER,
            FirebaseUploadPurpose.PARCEL_EVIDENCE_PHOTO => role == UserRole.ASSISTANT,
            FirebaseUploadPurpose.INCIDENT_PHOTO => role is UserRole.DRIVER or UserRole.ASSISTANT,
            FirebaseUploadPurpose.USER_AVATAR => true,
            _ => false,
        };

        if (!allowed)
            throw new ForbiddenException("FORBIDDEN", $"{role} cannot request {purpose} upload access.");
    }

    private static string BuildUploadPath(
        Guid userId,
        Guid? operatorId,
        FirebaseUploadPurpose purpose)
        => purpose switch
        {
            FirebaseUploadPurpose.VEHICLE_IMAGE => $"vehicles/{RequireOperatorId(operatorId):D}/",
            FirebaseUploadPurpose.OPERATOR_LOGO => $"operators/{RequireOperatorId(operatorId):D}/logo/",
            FirebaseUploadPurpose.PARCEL_PHOTO => $"parcels/{userId:D}/",
            FirebaseUploadPurpose.PARCEL_EVIDENCE_PHOTO
                => $"parcel-ops/{RequireOperatorId(operatorId):D}/{userId:D}/",
            FirebaseUploadPurpose.INCIDENT_PHOTO => $"incidents/{RequireOperatorId(operatorId):D}/{userId:D}/",
            FirebaseUploadPurpose.USER_AVATAR => $"avatars/{userId:D}/",
            _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
        };

    private static Guid RequireOperatorId(Guid? operatorId)
        => operatorId
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required for this upload purpose.");
}
