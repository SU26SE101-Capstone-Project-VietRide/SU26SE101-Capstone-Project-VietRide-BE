using System.Text.Json;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Exceptions;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Domain.ValueObjects;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Application.Features.Settlements.SettleTrip;

public sealed record TripSettlementResult(
    Guid SettlementId,
    string Status,
    long NetAmount,
    DateTimeOffset? SettledAt);

public sealed class TripSettlementService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOperatorTripSettlementRepository _settlements;
    private readonly IOperatorLedgerEntryRepository _ledger;
    private readonly IPlatformWalletRepository _platformWallets;
    private readonly IOperatorWalletRepository _operatorWallets;
    private readonly IOperatorWalletTransactionRepository _operatorTransactions;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFinancialActorPrivacyStore _actorPrivacy;
    private readonly IClock _clock;

    public TripSettlementService(
        IOperatorTripSettlementRepository settlements,
        IOperatorLedgerEntryRepository ledger,
        IPlatformWalletRepository platformWallets,
        IOperatorWalletRepository operatorWallets,
        IOperatorWalletTransactionRepository operatorTransactions,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IFinancialActorPrivacyStore actorPrivacy,
        IClock clock)
    {
        _settlements = settlements;
        _ledger = ledger;
        _platformWallets = platformWallets;
        _operatorWallets = operatorWallets;
        _operatorTransactions = operatorTransactions;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _actorPrivacy = actorPrivacy;
        _clock = clock;
    }

    public async Task<TripSettlementResult?> SettleAsync(
        Guid settlementId,
        OperatorTripSettlementMethod method,
        FinancialActorSnapshot? settledBy,
        bool conflictWhenAlreadyTerminal,
        CancellationToken cancellationToken)
    {
        var transactionCompleted = false;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (settledBy is not null
                && await _actorPrivacy.IsDeletedWithLockAsync(settledBy.UserId, cancellationToken))
            {
                throw new UnauthorizedAccessException("Financial actor account is deleted.");
            }

            var settlement = await _settlements.GetForUpdateAsync(settlementId, cancellationToken)
                ?? throw new CodedNotFoundException("TRIP_SETTLEMENT_NOT_FOUND", "Settlement was not found.");
            if (settlement.Status is OperatorTripSettlementStatus.SETTLED or OperatorTripSettlementStatus.CANCELLED)
            {
                if (conflictWhenAlreadyTerminal)
                {
                    throw new ConflictException(
                        "TRIP_SETTLEMENT_ALREADY_SETTLED",
                        "Settlement is already terminal.");
                }

                await _unitOfWork.RollbackAsync(cancellationToken);
                transactionCompleted = true;
                return null;
            }

            var now = _clock.UtcNow;
            if (method == OperatorTripSettlementMethod.AUTO_WEEKLY
                && settlement.Status != OperatorTripSettlementStatus.ELIGIBLE)
            {
                throw new ConflictException(
                    "TRIP_SETTLEMENT_NOT_ELIGIBLE",
                    "Settlement hold period has not elapsed.");
            }

            var netAmount = await _ledger.SumTripNetAmountAsync(
                settlement.OperatorId,
                settlement.TripId,
                cancellationToken);
            if (netAmount <= 0)
            {
                settlement.MarkCancelled(netAmount, method, now, settledBy);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
                transactionCompleted = true;
                return ToResult(settlement);
            }

            settlement.RefreshEligibility(netAmount, now);
            var canSettle = settlement.Status == OperatorTripSettlementStatus.ELIGIBLE
                || method == OperatorTripSettlementMethod.ADMIN_MANUAL
                    && settlement.Status == OperatorTripSettlementStatus.PENDING_HOLD;
            if (!canSettle)
                throw new ConflictException("TRIP_SETTLEMENT_NOT_ELIGIBLE", "Settlement hold period has not elapsed.");

            try
            {
                var platformTransaction = await _platformWallets.DebitAsync(
                    Money.FromRaw(netAmount),
                    PlatformWalletTransactionRef.TRIP_SETTLEMENT,
                    settlement.Id,
                    "Operator trip settlement",
                    cancellationToken);
                if (settledBy is not null)
                    platformTransaction.AssignUserActor(settledBy);
            }
            catch (InvalidOperationException exception)
            {
                if (settlement.Status == OperatorTripSettlementStatus.ELIGIBLE)
                {
                    settlement.RecordFailure("PLATFORM_WALLET_INSUFFICIENT_BALANCE", now);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await _unitOfWork.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                }

                throw new PlatformWalletInsufficientBalanceException(exception.Message);
            }

            var operatorWallet = await _operatorWallets.FindByOperatorIdAsync(
                settlement.OperatorId,
                cancellationToken);
            if (operatorWallet is null)
            {
                operatorWallet = OperatorWallet.Create(settlement.OperatorId);
                await _operatorWallets.AddAsync(operatorWallet, cancellationToken);
            }

            var balanceBefore = operatorWallet.Balance;
            operatorWallet.Credit(Money.FromRaw(netAmount));
            var operatorTransaction = OperatorWalletTransaction.Create(
                settlement.OperatorId,
                OperatorWalletTransactionType.CREDIT,
                Money.FromRaw(netAmount),
                balanceBefore,
                operatorWallet.Balance,
                OperatorWalletTransactionRef.TRIP_SETTLEMENT,
                settlement.Id,
                "Trip settlement");
            await _operatorTransactions.AddAsync(operatorTransaction, cancellationToken);
            settlement.MarkSettled(netAmount, method, now, settledBy, operatorTransaction.Id);

            var evt = new TripSettlementCompletedIntegrationEvent(
                settlement.Id,
                settlement.TripId,
                settlement.OperatorId,
                netAmount,
                method,
                now);
            await _outbox.EnqueueAsync(
                evt.EventType,
                JsonSerializer.Serialize(evt, JsonOptions),
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
            transactionCompleted = true;
            return ToResult(settlement);
        }
        catch
        {
            if (!transactionCompleted)
                await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static TripSettlementResult ToResult(OperatorTripSettlement settlement)
        => new(settlement.Id, settlement.Status.ToString(), settlement.NetAmount, settlement.SettledAt);
}
