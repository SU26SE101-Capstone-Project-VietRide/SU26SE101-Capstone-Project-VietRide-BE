using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Domain.ValueObjects;
using VietRide.Shared.Kernel.Identifiers;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Domain.Entities;

public sealed class OperatorTripSettlement : BaseEntity<Guid>, IBusinessCodeEntity
{
    string IBusinessCodeEntity.BusinessCodeConstraintName => "uq_operator_trip_settlements_code";
    private OperatorTripSettlement() { }

    public Guid OperatorId { get; private set; }
    public string? SettlementCode { get; private set; }
    public Guid TripId { get; private set; }
    public string? TripCode { get; private set; }
    public long NetAmount { get; private set; }
    public DateTimeOffset TripTerminalAt { get; private set; }
    public DateTimeOffset EligibleAt { get; private set; }
    public OperatorTripSettlementStatus Status { get; private set; }
    public OperatorTripSettlementMethod? SettlementMethod { get; private set; }
    public DateTimeOffset? SettledAt { get; private set; }
    public Guid? SettledByUserId { get; private set; }
    public bool OperatorSnapshotResolved { get; private set; }
    public string? OperatorName { get; private set; }
    public string? OperatorLogoUrl { get; private set; }
    public string? OperatorContactPhone { get; private set; }
    public bool SettledBySnapshotResolved { get; private set; }
    public string? SettledByDisplayName { get; private set; }
    public string? SettledByEmail { get; private set; }
    public string? SettledByRole { get; private set; }
    public Guid? WalletTransactionId { get; private set; }
    public int SettlementFailureCount { get; private set; }
    public DateTimeOffset? LastSettlementFailureAt { get; private set; }
    public string? ActiveFailureCode { get; private set; }
    public DateTimeOffset? FailureResolvedAt { get; private set; }

    public static OperatorTripSettlement CreatePending(
        Guid operatorId,
        Guid tripId,
        DateTimeOffset terminalAt,
        string? tripCode = null)
    {
        if (operatorId == Guid.Empty || tripId == Guid.Empty)
            throw new ArgumentException("Settlement operator and trip are required.");

        return new OperatorTripSettlement
        {
            Id = Guid.NewGuid(),
            SettlementCode = BusinessCodeGenerator.Generate("STL", terminalAt),
            OperatorId = operatorId,
            TripId = tripId,
            TripCode = NormalizeTripCode(tripCode),
            TripTerminalAt = terminalAt,
            EligibleAt = terminalAt.AddDays(7),
            Status = OperatorTripSettlementStatus.PENDING_HOLD,
        };
    }

    public void SetTripCode(string tripCode)
    {
        var normalized = NormalizeTripCode(tripCode)
            ?? throw new ArgumentException("Trip code is required.", nameof(tripCode));
        if (TripCode is not null && !string.Equals(TripCode, normalized, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Settlement Trip code is immutable once assigned.");
        }

        TripCode = normalized;
    }

    public void BackfillBusinessCodes(string? tripCode = null)
    {
        SettlementCode ??= BusinessCodeGenerator.Generate("STL", TripTerminalAt);
        if (TripCode is null && tripCode is not null)
        {
            SetTripCode(tripCode);
        }
    }

    void IBusinessCodeEntity.RegenerateBusinessCode()
        => SettlementCode = BusinessCodeGenerator.Generate("STL", TripTerminalAt);

    public void RefreshEligibility(long netAmount, DateTimeOffset now)
    {
        if (Status is OperatorTripSettlementStatus.SETTLED or OperatorTripSettlementStatus.CANCELLED)
            return;

        NetAmount = netAmount;
        if (netAmount <= 0)
        {
            MarkCancelled(netAmount, OperatorTripSettlementMethod.AUTO_WEEKLY, now, null);
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
        FinancialActorSnapshot? settledBy,
        Guid walletTransactionId)
    {
        var canSettle = Status == OperatorTripSettlementStatus.ELIGIBLE
            || method == OperatorTripSettlementMethod.ADMIN_MANUAL
                && Status == OperatorTripSettlementStatus.PENDING_HOLD;
        if (!canSettle)
            throw new InvalidOperationException("Settlement is not eligible.");
        if (netAmount <= 0 || walletTransactionId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(netAmount));

        NetAmount = netAmount;
        Status = OperatorTripSettlementStatus.SETTLED;
        SettlementMethod = method;
        SettledAt = settledAt;
        SettledBySnapshotResolved = true;
        if (settledBy is not null)
        {
            SettledByUserId = settledBy.UserId;
            SettledByDisplayName = settledBy.DisplayName;
            SettledByEmail = settledBy.Email;
            SettledByRole = settledBy.Role;
        }
        WalletTransactionId = walletTransactionId;
        if (ActiveFailureCode is not null)
        {
            ActiveFailureCode = null;
            FailureResolvedAt = settledAt;
        }
    }

    private static string? NormalizeTripCode(string? tripCode)
    {
        if (tripCode is null)
        {
            return null;
        }

        var normalized = tripCode.Trim().ToUpperInvariant();
        if (normalized.Length == 0 || normalized.Length > 30)
        {
            throw new ArgumentException("Trip code must contain 1 to 30 characters.", nameof(tripCode));
        }

        return normalized;
    }

    public void MarkCancelled(
        long netAmount,
        OperatorTripSettlementMethod method,
        DateTimeOffset settledAt,
        FinancialActorSnapshot? settledBy)
    {
        if (Status is not (OperatorTripSettlementStatus.PENDING_HOLD or OperatorTripSettlementStatus.ELIGIBLE))
            throw new InvalidOperationException("Only pending settlements can be cancelled.");
        if (netAmount > 0)
            throw new ArgumentOutOfRangeException(nameof(netAmount));

        NetAmount = netAmount;
        Status = OperatorTripSettlementStatus.CANCELLED;
        SettlementMethod = method;
        SettledAt = settledAt;
        SettledBySnapshotResolved = true;
        if (settledBy is not null)
        {
            SettledByUserId = settledBy.UserId;
            SettledByDisplayName = settledBy.DisplayName;
            SettledByEmail = settledBy.Email;
            SettledByRole = settledBy.Role;
        }

        if (ActiveFailureCode is not null)
        {
            ActiveFailureCode = null;
            FailureResolvedAt = settledAt;
        }
    }

    public void SetOperatorSnapshot(FinancialOperatorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.OperatorId != OperatorId)
            throw new ArgumentException("Operator snapshot does not belong to this settlement.", nameof(snapshot));

        OperatorName = snapshot.Name;
        OperatorLogoUrl = snapshot.LogoUrl;
        OperatorContactPhone = snapshot.ContactPhone;
        OperatorSnapshotResolved = true;
    }

    public void MarkOperatorSnapshotUnavailable()
    {
        OperatorName = null;
        OperatorLogoUrl = null;
        OperatorContactPhone = null;
        OperatorSnapshotResolved = true;
    }

    public void SetSettledBySnapshot(FinancialActorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (SettledByUserId.HasValue && SettledByUserId != snapshot.UserId)
            throw new ArgumentException("Actor snapshot does not belong to this settlement.", nameof(snapshot));

        SettledByUserId = snapshot.UserId;
        SettledByDisplayName = snapshot.DisplayName;
        SettledByEmail = snapshot.Email;
        SettledByRole = snapshot.Role;
        SettledBySnapshotResolved = true;
    }

    public void MarkSettledBySnapshotUnavailable()
    {
        SettledByDisplayName = null;
        SettledByEmail = null;
        SettledByRole = null;
        SettledBySnapshotResolved = true;
    }
}
