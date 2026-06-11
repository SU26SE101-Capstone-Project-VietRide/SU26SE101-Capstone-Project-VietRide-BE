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
        {
            throw new ForbiddenException("FORBIDDEN", eligibility.Message ?? "Operator is not allowed to write Trip stops.");
        }

        throw new ValidationException(
            eligibility.Message ?? "Operator logical FK validation failed.",
            [new ValidationError("operatorId", eligibility.Message ?? "Operator logical FK validation failed.")]);
    }
}
