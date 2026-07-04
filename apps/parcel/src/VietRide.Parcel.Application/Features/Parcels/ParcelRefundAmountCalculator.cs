using VietRide.Parcel.Application.Abstractions.ServiceClients;

namespace VietRide.Parcel.Application.Features.Parcels;

public static class ParcelRefundAmountCalculator
{
    public static async Task<long> CalculateRefundAsync(
        IIdentityServiceClient identityClient,
        Guid operatorId,
        long grossAmount,
        CancellationToken cancellationToken)
    {
        var policyOutcome = await identityClient.GetOperatorInfoAsync(operatorId, cancellationToken);
        var policy = policyOutcome.Kind == OperatorLookupOutcomeKind.Success
            ? policyOutcome.OperatorInfo!.ParcelNoShowPolicy
            : ParcelNoShowPolicy.Default;

        return ApplyNoShowFee(grossAmount, policy);
    }

    public static long ApplyNoShowFee(long grossAmount, ParcelNoShowPolicy policy)
    {
        if (grossAmount <= 0)
            return 0;

        var percent = Math.Clamp(policy.NoShowFeePercent, 0, 100);
        var netAmount = grossAmount * (100m - percent) / 100m;
        return Math.Max(0, (long)Math.Round(netAmount, 0, MidpointRounding.AwayFromZero));
    }
}
