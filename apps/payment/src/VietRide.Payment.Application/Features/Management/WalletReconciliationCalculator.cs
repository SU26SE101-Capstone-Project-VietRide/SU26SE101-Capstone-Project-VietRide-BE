using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Application.Features.Management;

public sealed record WalletReconciliationSettlementMarker(
    Guid OperatorId,
    Guid TripId,
    OperatorTripSettlementStatus Status);

public sealed record OperatorWalletReconciliationProjection(
    Guid OperatorId,
    long OutstandingPayableVnd,
    long AwaitingTripCompletionPayableVnd,
    long PendingHoldPayableVnd,
    long EligibleForSettlementVnd);

public sealed record AdminWalletReconciliationProjection(
    long OutstandingOperatorPayableVnd,
    long AwaitingTripCompletionVnd,
    long PendingHoldVnd,
    long EligibleForSettlementVnd,
    int EligibleOperatorCount);

public static class WalletReconciliationCalculator
{
    public static IReadOnlyList<OperatorWalletReconciliationProjection> Calculate(
        IReadOnlyCollection<TripFinancialProjection> projections,
        IReadOnlyCollection<WalletReconciliationSettlementMarker> settlements)
    {
        var markers = settlements.ToDictionary(item => (item.OperatorId, item.TripId));
        return projections
            .GroupBy(item => item.OperatorId)
            .Select(group => CalculateOperator(group.Key, group, markers))
            .OrderBy(item => item.OperatorId)
            .ToArray();
    }

    public static AdminWalletReconciliationProjection Aggregate(
        IReadOnlyCollection<OperatorWalletReconciliationProjection> operators)
    {
        var awaiting = operators.Sum(item => item.AwaitingTripCompletionPayableVnd);
        var pending = operators.Sum(item => item.PendingHoldPayableVnd);
        var eligible = operators.Sum(item => item.EligibleForSettlementVnd);
        return new AdminWalletReconciliationProjection(
            checked(awaiting + pending + eligible),
            awaiting,
            pending,
            eligible,
            operators.Count(item => item.EligibleForSettlementVnd > 0));
    }

    private static OperatorWalletReconciliationProjection CalculateOperator(
        Guid operatorId,
        IEnumerable<TripFinancialProjection> projections,
        IReadOnlyDictionary<(Guid OperatorId, Guid TripId), WalletReconciliationSettlementMarker> markers)
    {
        long awaiting = 0;
        long pending = 0;
        long eligible = 0;
        foreach (var projection in projections)
        {
            var amount = Math.Max(projection.NetEntitlementAmount, 0);
            if (amount == 0)
                continue;

            if (!markers.TryGetValue((operatorId, projection.TripId), out var marker))
            {
                awaiting = checked(awaiting + amount);
                continue;
            }

            switch (marker.Status)
            {
                case OperatorTripSettlementStatus.PENDING_HOLD:
                    pending = checked(pending + amount);
                    break;
                case OperatorTripSettlementStatus.ELIGIBLE:
                    eligible = checked(eligible + amount);
                    break;
                case OperatorTripSettlementStatus.SETTLED:
                case OperatorTripSettlementStatus.CANCELLED:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(marker), marker.Status, "Unsupported settlement status.");
            }
        }

        return new OperatorWalletReconciliationProjection(
            operatorId,
            checked(awaiting + pending + eligible),
            awaiting,
            pending,
            eligible);
    }
}
