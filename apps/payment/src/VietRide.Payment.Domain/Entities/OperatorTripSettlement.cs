using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Domain.Entities;

public sealed class OperatorTripSettlement : BaseEntity<Guid>
{
    private OperatorTripSettlement() { }

    public Guid OperatorId { get; private set; }
    public Guid TripId { get; private set; }
    public long NetAmount { get; private set; }
    public DateTimeOffset TripTerminalAt { get; private set; }
    public DateTimeOffset EligibleAt { get; private set; }
    public OperatorTripSettlementStatus Status { get; private set; }
    public OperatorTripSettlementMethod? SettlementMethod { get; private set; }
    public DateTimeOffset? SettledAt { get; private set; }
    public Guid? SettledByUserId { get; private set; }
    public Guid? WalletTransactionId { get; private set; }
    public int SettlementFailureCount { get; private set; }
    public DateTimeOffset? LastSettlementFailureAt { get; private set; }
    public string? ActiveFailureCode { get; private set; }
    public DateTimeOffset? FailureResolvedAt { get; private set; }

    public static OperatorTripSettlement CreatePending(
        Guid operatorId,
        Guid tripId,
        DateTimeOffset terminalAt)
    {
        if (operatorId == Guid.Empty || tripId == Guid.Empty)
            throw new ArgumentException("Settlement operator and trip are required.");

        return new OperatorTripSettlement
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            TripId = tripId,
            TripTerminalAt = terminalAt,
            EligibleAt = terminalAt.AddDays(7),
            Status = OperatorTripSettlementStatus.PENDING_HOLD,
        };
    }

    public void RefreshEligibility(long netAmount, DateTimeOffset now)
    {
        if (Status is OperatorTripSettlementStatus.SETTLED or OperatorTripSettlementStatus.CANCELLED)
            return;

        NetAmount = netAmount;
        if (netAmount <= 0)
        {
            Status = OperatorTripSettlementStatus.CANCELLED;
            SettlementMethod = OperatorTripSettlementMethod.AUTO_WEEKLY;
            SettledAt = now;
            ActiveFailureCode = null;
            return;
        }

        Status = now >= EligibleAt
            ? OperatorTripSettlementStatus.ELIGIBLE
            : OperatorTripSettlementStatus.PENDING_HOLD;
    }

    public void RecordFailure(string failureCode, DateTimeOffset failedAt)
    {
        if (Status != OperatorTripSettlementStatus.ELIGIBLE)
            throw new InvalidOperationException("Only eligible settlements can record settlement failure.");
        if (string.IsNullOrWhiteSpace(failureCode))
            throw new ArgumentException("Failure code is required.", nameof(failureCode));

        SettlementFailureCount++;
        LastSettlementFailureAt = failedAt;
        ActiveFailureCode = failureCode.Trim();
        FailureResolvedAt = null;
    }

    public void MarkSettled(
        long netAmount,
        OperatorTripSettlementMethod method,
        DateTimeOffset settledAt,
        Guid? settledByUserId,
        Guid walletTransactionId)
    {
        if (Status != OperatorTripSettlementStatus.ELIGIBLE)
            throw new InvalidOperationException("Settlement is not eligible.");
        if (netAmount <= 0 || walletTransactionId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(netAmount));

        NetAmount = netAmount;
        Status = OperatorTripSettlementStatus.SETTLED;
        SettlementMethod = method;
        SettledAt = settledAt;
        SettledByUserId = settledByUserId;
        WalletTransactionId = walletTransactionId;
        if (ActiveFailureCode is not null)
        {
            ActiveFailureCode = null;
            FailureResolvedAt = settledAt;
        }
    }
}
