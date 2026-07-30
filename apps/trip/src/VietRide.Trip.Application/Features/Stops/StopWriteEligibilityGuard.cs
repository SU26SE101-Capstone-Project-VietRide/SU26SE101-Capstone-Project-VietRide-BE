using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Application.Features.Stops;

internal static class StopWriteEligibilityGuard
{
    public static async Task ValidateOperatorCanWriteAsync(
        IIdentityInternalClient identityInternalClient,
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        var eligibility = await identityInternalClient.ValidateOperatorCanWriteAsync(operatorId, cancellationToken);
        if (eligibility.IsAllowed)
        {
            return;
        }

        if (eligibility.FailureStatusCode == 403)
            throw new ForbiddenException(
                eligibility.ErrorCode ?? "FORBIDDEN",
                eligibility.Message ?? "Operator is not allowed to write Trip stops.");

        if (eligibility.FailureStatusCode == 402)
        {
            throw new TripSubscriptionWriteBlockedException(
                eligibility.FailureStatusCode.Value,
                eligibility.ErrorCode ?? "FORBIDDEN",
                eligibility.Message ?? "Operator is not allowed to write Trip stops.");
        }

        if (eligibility.FailureStatusCode == 503)
        {
            throw new TripSubscriptionWriteBlockedException(
                eligibility.FailureStatusCode.Value,
                eligibility.ErrorCode ?? "UPSTREAM_UNAVAILABLE",
                eligibility.Message ?? "Identity is unavailable.");
        }

        if (eligibility.FailureStatusCode == 409)
            throw new CodedConflictException(
                eligibility.ErrorCode ?? "CONFLICT",
                eligibility.Message ?? "Operator write is blocked.");

        throw new ValidationException(
            eligibility.Message ?? "Operator logical FK validation failed.",
            [new ValidationError("operatorId", eligibility.Message ?? "Operator logical FK validation failed.")]);
    }

    public static async Task ValidateOperatorSubscriptionCanWriteAsync(
        IIdentityInternalClient identityInternalClient,
        Guid operatorId,
        bool requireShuttleModule,
        CancellationToken cancellationToken)
    {
        var eligibility = await identityInternalClient.ValidateOperatorSubscriptionCanWriteAsync(
            operatorId,
            requireShuttleModule,
            cancellationToken) ?? OperatorWriteEligibilityValidation.Allowed();
        if (eligibility.IsAllowed)
            return;

        if (eligibility.FailureStatusCode is 402 or 403 or 503)
            throw new TripSubscriptionWriteBlockedException(
                eligibility.FailureStatusCode.Value,
                eligibility.ErrorCode ?? (eligibility.FailureStatusCode == 503
                    ? "UPSTREAM_UNAVAILABLE"
                    : "FORBIDDEN"),
                eligibility.Message ?? "Operator subscription blocks this write.");
        if (eligibility.FailureStatusCode == 409)
            throw new CodedConflictException(
                eligibility.ErrorCode ?? "CONFLICT",
                eligibility.Message ?? "Operator subscription payment is pending.");

        throw new ValidationException(
            eligibility.Message ?? "Operator subscription validation failed.",
            [new ValidationError("operatorId", eligibility.Message ?? "Operator subscription validation failed.")]);
    }
}
