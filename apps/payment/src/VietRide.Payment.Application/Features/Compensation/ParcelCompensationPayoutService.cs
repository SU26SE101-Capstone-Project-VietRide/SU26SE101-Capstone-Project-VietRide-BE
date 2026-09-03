using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<ParcelCompensationPayoutService> _logger;

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
        IClock clock,
        ILogger<ParcelCompensationPayoutService> logger)
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
        _logger = logger;
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
            var payout = existing ?? ParcelCompensationPayout.Create(
                claimId,
                parcelId,
                tripId,
                operatorId,
                beneficiaryUserId,
                amountVnd,
                sourceEventId);
            if (existing is not null
                && (payout.ParcelId != parcelId
                    || payout.TripId != tripId
                    || payout.OperatorId != operatorId
                    || payout.BeneficiaryUserId != beneficiaryUserId
                    || payout.AmountVnd != amountVnd))
                throw new InvalidOperationException(
                    "A replayed parcel compensation claim does not match the persisted payout snapshot.");
            if (existing?.PaidEventId.HasValue == true)
                return false;

            payout.EnsureSourceEvent(sourceEventId);
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
            var amount = Money.FromRaw(amountVnd);
            var existingSource = await FindExistingFundingSourceAsync(payout, amount, cancellationToken);
            var source = existingSource
                ?? await ResolveFundingSourceAsync(operatorId, tripId, cancellationToken);
            var wasFundingPending = payout.Status == ParcelCompensationPayoutStatus.FUNDING_PENDING;
            var funded = existingSource.HasValue
                || await TryDebitFundingAsync(source, payout, amount, cancellationToken);
            if (!funded)
            {
                if (walletReplay is not null)
                {
                    throw new InvalidOperationException(
                        "Passenger compensation exists without its funding debit; recovery will retry when funds are available.");
                }

                payout.MarkFundingPending();
                if (!wasFundingPending)
                    await EnqueueAsync(FundingPendingEventType, payout, sourceEventId, cancellationToken);
                return true;
            }

            var passengerTransaction = walletReplay;
            if (passengerTransaction is null)
            {
                await _wallets.EnsureBootstrapWalletAsync(beneficiaryUserId, cancellationToken);
                passengerTransaction = await _wallets.CreditRefundAsync(
                    beneficiaryUserId,
                    amount,
                    WalletTransactionRef.PARCEL_COMPENSATION,
                    claimId,
                    cancellationToken);
            }
            else if (passengerTransaction.UserId != beneficiaryUserId
                || passengerTransaction.Type != WalletTransactionType.CREDIT
                || passengerTransaction.Amount != amount)
            {
                throw new InvalidOperationException(
                    "Persisted passenger compensation transaction does not match the payout snapshot.");
            }

            if (!await _ledger.HasSourceEntryAsync(sourceEventId, parcelId, cancellationToken))
            {
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
            }

            payout.MarkPaid(source, passengerTransaction.Id, payout.PaidAt ?? _clock.UtcNow);
            var paidEventId = await EnqueueAsync(PaidEventType, payout, sourceEventId, cancellationToken);
            payout.MarkPaidEventEnqueued(paidEventId);
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
                SourceEventId = x.SourceEventId ?? x.ClaimId,
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
                payout.SourceEventId,
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

    public async Task<int> ReconcileIncompletePaidAsync(int maxBatch, CancellationToken cancellationToken)
    {
        var incomplete = await _payouts.QueryNoTracking()
            .Where(x => x.Status == ParcelCompensationPayoutStatus.PAID
                && x.SourceEventId != null
                && x.PaidEventId == null)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Take(Math.Clamp(maxBatch, 1, 200))
            .Select(x => new
            {
                SourceEventId = x.SourceEventId ?? x.ClaimId,
                x.ClaimId,
                x.ParcelId,
                x.TripId,
                x.OperatorId,
                x.BeneficiaryUserId,
                x.AmountVnd,
            })
            .ToArrayAsync(cancellationToken);

        var repaired = 0;
        foreach (var payout in incomplete)
        {
            try
            {
                await ProcessApprovedClaimAsync(
                    payout.SourceEventId,
                    payout.ClaimId,
                    payout.ParcelId,
                    payout.TripId,
                    payout.OperatorId,
                    payout.BeneficiaryUserId,
                    payout.AmountVnd,
                    cancellationToken);
                repaired++;
            }
            catch (InvalidOperationException exception)
            {
                // A passenger credit without an available funding source must remain visible to
                // the next reconciliation run; never fabricate a debit or publish PAID early.
                _logger.LogWarning(
                    exception,
                    "Could not reconcile incomplete parcel compensation payout {ClaimId}.",
                    payout.ClaimId);
            }
        }

        return repaired;
    }

    private async Task<ParcelCompensationFundingSource?> FindExistingFundingSourceAsync(
        ParcelCompensationPayout payout,
        Money amount,
        CancellationToken cancellationToken)
    {
        var platformTransaction = await _platformWallets.FindTransactionByReferenceAsync(
            PlatformWalletTransactionRef.PARCEL_COMPENSATION,
            payout.ClaimId,
            cancellationToken);
        var operatorTransaction = await _operatorTransactions.FindByReferenceAsync(
            payout.OperatorId,
            OperatorWalletTransactionRef.PARCEL_COMPENSATION,
            payout.ClaimId,
            cancellationToken);

        if (platformTransaction is not null && operatorTransaction is not null)
            throw new InvalidOperationException("Parcel compensation was debited from both funding sources.");
        if (platformTransaction is not null)
        {
            if (platformTransaction.Type != PlatformWalletTransactionType.DEBIT
                || platformTransaction.Amount != amount)
            {
                throw new InvalidOperationException(
                    "Persisted platform compensation debit does not match the payout snapshot.");
            }

            return ParcelCompensationFundingSource.PLATFORM_HOLDING;
        }

        if (operatorTransaction is not null)
        {
            if (operatorTransaction.Type != OperatorWalletTransactionType.DEBIT
                || operatorTransaction.Amount != amount)
            {
                throw new InvalidOperationException(
                    "Persisted operator compensation debit does not match the payout snapshot.");
            }

            return ParcelCompensationFundingSource.OPERATOR_WALLET;
        }

        return null;
    }

    private async Task<ParcelCompensationFundingSource> ResolveFundingSourceAsync(
        Guid operatorId,
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var settlement = await _settlements.FindByOperatorTripAsync(operatorId, tripId, cancellationToken);
        return settlement is null || settlement.Status != OperatorTripSettlementStatus.SETTLED
            ? ParcelCompensationFundingSource.PLATFORM_HOLDING
            : ParcelCompensationFundingSource.OPERATOR_WALLET;
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
            await _platformWallets.DebitWithLinksAsync(
                amount,
                PlatformWalletTransactionRef.PARCEL_COMPENSATION,
                payout.ClaimId,
                $"Parcel compensation for operator {payout.OperatorId:D}",
                [new PlatformWalletTransactionLinkInput(
                    PlatformWalletTransactionLinkType.PARCEL_CLAIM,
                    amount.Amount,
                    payout.OperatorId,
                    payout.TripId,
                    payout.ClaimId)],
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
                "Parcel compensation",
                _clock.UtcNow),
            cancellationToken);
        return true;
    }

    private async Task<Guid> EnqueueAsync(
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
        await _outbox.EnqueueAsync(eventId, eventType, payload, cancellationToken);
        return eventId;
    }
}
