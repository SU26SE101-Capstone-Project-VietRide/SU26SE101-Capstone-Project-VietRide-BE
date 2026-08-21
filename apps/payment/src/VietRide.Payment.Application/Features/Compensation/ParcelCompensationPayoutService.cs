using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Application.Features.Compensation;

public sealed class ParcelCompensationPayoutService
{
    public const string PaidEventType = "payment.parcel_compensation.paid";
    public const string FundingPendingEventType = "payment.parcel_compensation.funding_pending";

    private readonly IParcelCompensationPayoutRepository _payouts;
    private readonly IWalletRepository _wallets;
    private readonly IPlatformWalletRepository _platformWallets;
    private readonly IOperatorWalletRepository _operatorWallets;
    private readonly IOperatorWalletTransactionRepository _operatorTransactions;
    private readonly IOperatorLedgerEntryRepository _ledger;
    private readonly IOperatorTripSettlementRepository _settlements;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ParcelCompensationPayoutService(
        IParcelCompensationPayoutRepository payouts,
        IWalletRepository wallets,
        IPlatformWalletRepository platformWallets,
        IOperatorWalletRepository operatorWallets,
        IOperatorWalletTransactionRepository operatorTransactions,
        IOperatorLedgerEntryRepository ledger,
        IOperatorTripSettlementRepository settlements,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _payouts = payouts;
        _wallets = wallets;
        _platformWallets = platformWallets;
        _operatorWallets = operatorWallets;
        _operatorTransactions = operatorTransactions;
        _ledger = ledger;
        _settlements = settlements;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public Task ProcessApprovedClaimAsync(
        Guid sourceEventId,
        Guid claimId,
        Guid parcelId,
        Guid tripId,
        Guid operatorId,
        Guid beneficiaryUserId,
        long amountVnd,
        CancellationToken cancellationToken)
        => _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var existing = await _payouts.FindByClaimIdAsync(claimId, cancellationToken);
            if (existing?.Status == ParcelCompensationPayoutStatus.PAID)
                return false;

            var payout = existing ?? ParcelCompensationPayout.Create(
                claimId,
                parcelId,
                tripId,
                operatorId,
                beneficiaryUserId,
                amountVnd);
            if (existing is not null
                && (payout.ParcelId != parcelId
                    || payout.TripId != tripId
                    || payout.OperatorId != operatorId
                    || payout.BeneficiaryUserId != beneficiaryUserId
                    || payout.AmountVnd != amountVnd))
                throw new InvalidOperationException(
                    "A replayed parcel compensation claim does not match the persisted payout snapshot.");
            if (existing is null)
                await _payouts.AddAsync(payout, cancellationToken);

            await _wallets.AcquireWalletTransactionReferenceLockAsync(
                WalletTransactionRef.PARCEL_COMPENSATION,
                claimId,
                cancellationToken);
            var walletReplay = await _wallets.FindTransactionByReferenceAsync(
                WalletTransactionRef.PARCEL_COMPENSATION,
                claimId,
                cancellationToken);
            if (walletReplay is not null)
            {
                payout.MarkPaid(
                    payout.FundingSource ?? ParcelCompensationFundingSource.OPERATOR_WALLET,
                    walletReplay.Id,
                    payout.PaidAt ?? _clock.UtcNow);
                return false;
            }

            var settlement = await _settlements.FindByOperatorTripAsync(operatorId, tripId, cancellationToken);
            var source = settlement is null || settlement.Status != OperatorTripSettlementStatus.SETTLED
                ? ParcelCompensationFundingSource.PLATFORM_HOLDING
                : ParcelCompensationFundingSource.OPERATOR_WALLET;
            var amount = Money.FromRaw(amountVnd);
            var wasFundingPending = payout.Status == ParcelCompensationPayoutStatus.FUNDING_PENDING;
            var funded = await TryDebitFundingAsync(source, payout, amount, cancellationToken);
            if (!funded)
            {
                payout.MarkFundingPending();
                if (!wasFundingPending)
                    await EnqueueAsync(FundingPendingEventType, payout, sourceEventId, cancellationToken);
                return true;
            }

            await _wallets.EnsureBootstrapWalletAsync(beneficiaryUserId, cancellationToken);
            var passengerTransaction = await _wallets.CreditRefundAsync(
                beneficiaryUserId,
                amount,
                WalletTransactionRef.PARCEL_COMPENSATION,
                claimId,
                cancellationToken);
            var ledgerEntry = OperatorLedgerEntry.Create(
                operatorId,
                tripId,
                OperatorLedgerEntryType.PARCEL_COMPENSATION,
                -amountVnd,
                OperatorLedgerReferenceType.PARCEL,
                parcelId,
                sourceEventId,
                $"Parcel claim compensation {claimId:D}");
            await _ledger.AddAsync(ledgerEntry, cancellationToken);

            payout.MarkPaid(source, passengerTransaction.Id, _clock.UtcNow);
            await EnqueueAsync(PaidEventType, payout, sourceEventId, cancellationToken);
            return true;
        }, cancellationToken);

    public async Task<int> RetryFundingPendingAsync(int maxBatch, CancellationToken cancellationToken)
    {
        var pending = await _payouts.QueryNoTracking()
            .Where(x => x.Status == ParcelCompensationPayoutStatus.FUNDING_PENDING)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Take(Math.Clamp(maxBatch, 1, 200))
            .Select(x => new
            {
                x.ClaimId,
                x.ParcelId,
                x.TripId,
                x.OperatorId,
                x.BeneficiaryUserId,
                x.AmountVnd,
            })
            .ToArrayAsync(cancellationToken);
        foreach (var payout in pending)
        {
            await ProcessApprovedClaimAsync(
                payout.ClaimId,
                payout.ClaimId,
                payout.ParcelId,
                payout.TripId,
                payout.OperatorId,
                payout.BeneficiaryUserId,
                payout.AmountVnd,
                cancellationToken);
        }

        return pending.Length;
    }

    private async Task<bool> TryDebitFundingAsync(
        ParcelCompensationFundingSource source,
        ParcelCompensationPayout payout,
        Money amount,
        CancellationToken cancellationToken)
    {
        if (source == ParcelCompensationFundingSource.PLATFORM_HOLDING)
        {
            var operatorHolding = await _ledger.SumTripNetAmountAsync(
                payout.OperatorId,
                payout.TripId,
                cancellationToken);
            if (operatorHolding < amount.Amount)
                return false;
            var platform = await _platformWallets.QueryNoTracking().SingleAsync(cancellationToken);
            if (platform.Balance < amount)
                return false;
            await _platformWallets.DebitAsync(
                amount,
                PlatformWalletTransactionRef.PARCEL_COMPENSATION,
                payout.ClaimId,
                $"Parcel compensation for operator {payout.OperatorId:D}",
                cancellationToken);
            return true;
        }

        var wallet = await _operatorWallets.FindByOperatorIdAsync(payout.OperatorId, cancellationToken);
        if (wallet is null || wallet.Balance < amount)
            return false;
        var before = wallet.Balance;
        wallet.Debit(amount);
        _operatorWallets.Update(wallet);
        await _operatorTransactions.AddAsync(
            OperatorWalletTransaction.Create(
                payout.OperatorId,
                OperatorWalletTransactionType.DEBIT,
                amount,
                before,
                wallet.Balance,
                OperatorWalletTransactionRef.PARCEL_COMPENSATION,
                payout.ClaimId,
                "Parcel compensation"),
            cancellationToken);
        return true;
    }

    private Task EnqueueAsync(
        string eventType,
        ParcelCompensationPayout payout,
        Guid sourceEventId,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            eventId,
            occurredAt = _clock.UtcNow,
            sourceEventId,
            payoutId = payout.Id,
            claimId = payout.ClaimId,
            parcelId = payout.ParcelId,
            operatorId = payout.OperatorId,
            beneficiaryUserId = payout.BeneficiaryUserId,
            amountVnd = payout.AmountVnd,
            status = payout.Status.ToString(),
            fundingSource = payout.FundingSource?.ToString(),
            walletTransactionId = payout.WalletTransactionId,
        });
        return _outbox.EnqueueAsync(eventId, eventType, payload, cancellationToken);
    }
}
