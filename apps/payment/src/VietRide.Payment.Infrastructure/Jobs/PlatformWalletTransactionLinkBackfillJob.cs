using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Application.Services;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Infrastructure.Jobs;

public sealed class PlatformWalletTransactionLinkBackfillJob
{
    public const string RecurringJobId = "payment.platform-wallet-link-backfill";
    public const int BatchSize = 100;

    private readonly PaymentDbContext db;
    private readonly ILogger<PlatformWalletTransactionLinkBackfillJob> logger;

    public PlatformWalletTransactionLinkBackfillJob(
        PaymentDbContext db,
        ILogger<PlatformWalletTransactionLinkBackfillJob> logger)
    {
        this.db = db;
        this.logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var transactions = await LoadRotatingBatchAsync(cancellationToken);
        var linked = 0;
        foreach (var transaction in transactions)
        {
            try
            {
                var inputs = await ResolveLinksAsync(transaction, cancellationToken);
                if (inputs.Count == 0 || inputs.Sum(item => item.AllocatedAmount) != transaction.Amount.Amount)
                    continue;

                var links = inputs.Select(item => PlatformWalletTransactionLink.Create(
                    transaction.Id,
                    item.LinkType,
                    item.AllocatedAmount,
                    item.OperatorId,
                    item.TripId,
                    item.ReferenceId,
                    item.ReferenceCode)).ToArray();
                await db.PlatformWalletTransactionLinks.AddRangeAsync(links, cancellationToken);
                linked++;
            }
            catch (Exception exception) when (exception is ArgumentException
                or InvalidOperationException
                or OverflowException)
            {
                logger.LogWarning(
                    exception,
                    "Skipped unprovable platform wallet transaction {TransactionId} during link backfill.",
                    transaction.Id);
            }
        }

        if (linked > 0)
            await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Backfilled provable links for {LinkedCount} of {CandidateCount} platform wallet transactions.",
            linked,
            transactions.Count);
    }

    private async Task<IReadOnlyList<PlatformWalletTransaction>> LoadRotatingBatchAsync(
        CancellationToken cancellationToken)
    {
        // A random UUID pivot keeps every run bounded without letting permanently
        // unverifiable legacy rows at the start of the table starve later rows.
        var pivot = Guid.NewGuid();
        var first = await db.PlatformWalletTransactions
            .FromSqlInterpolated($"""
                SELECT movement.*
                FROM vietride_payment.platform_wallet_transactions AS movement
                WHERE movement.id >= {pivot}
                  AND movement.reference_type <> 'MANUAL_ADJUSTMENT'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM vietride_payment.platform_wallet_transaction_links AS link
                      WHERE link.platform_wallet_transaction_id = movement.id)
                ORDER BY movement.id
                LIMIT {BatchSize}
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (first.Count == BatchSize)
            return first;

        var remaining = BatchSize - first.Count;
        var wrapped = await db.PlatformWalletTransactions
            .FromSqlInterpolated($"""
                SELECT movement.*
                FROM vietride_payment.platform_wallet_transactions AS movement
                WHERE movement.id < {pivot}
                  AND movement.reference_type <> 'MANUAL_ADJUSTMENT'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM vietride_payment.platform_wallet_transaction_links AS link
                      WHERE link.platform_wallet_transaction_id = movement.id)
                ORDER BY movement.id
                LIMIT {remaining}
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        first.AddRange(wrapped);
        return first;
    }

    private async Task<IReadOnlyList<PlatformWalletTransactionLinkInput>> ResolveLinksAsync(
        PlatformWalletTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!transaction.ReferenceId.HasValue)
            return [];

        return transaction.ReferenceType switch
        {
            PlatformWalletTransactionRef.BOOKING_PAYMENT_HOLD
                or PlatformWalletTransactionRef.PARCEL_PAYMENT_HOLD
                or PlatformWalletTransactionRef.PARCEL_ADDITIONAL_PAYMENT_HOLD
                => await ResolvePaymentHoldAsync(transaction.ReferenceId.Value, cancellationToken),
            PlatformWalletTransactionRef.BOOKING_REFUND
                or PlatformWalletTransactionRef.PARCEL_REFUND
                => await ResolveRefundAsync(
                    transaction.ReferenceId.Value,
                    transaction.Amount.Amount,
                    cancellationToken),
            PlatformWalletTransactionRef.SUBSCRIPTION_PAYMENT
                => await ResolveSubscriptionAsync(transaction.ReferenceId.Value, transaction.Amount.Amount, cancellationToken),
            PlatformWalletTransactionRef.TRIP_SETTLEMENT
                => await ResolveSettlementAsync(transaction.ReferenceId.Value, transaction.Amount.Amount, cancellationToken),
            PlatformWalletTransactionRef.PARCEL_COMPENSATION
                => await ResolveCompensationAsync(transaction.ReferenceId.Value, transaction.Amount.Amount, cancellationToken),
            _ => [],
        };
    }

    private async Task<IReadOnlyList<PlatformWalletTransactionLinkInput>> ResolvePaymentHoldAsync(
        Guid referenceId,
        CancellationToken cancellationToken)
    {
        var payment = await db.Payments.AsNoTracking()
            .Where(item => item.ReferenceId == referenceId
                && (item.Status == PaymentStatus.SUCCEEDED || item.Status == PaymentStatus.REFUNDED)
                && item.Context != "{}")
            .OrderByDescending(item => item.SucceededAt)
            .FirstOrDefaultAsync(cancellationToken);
        return TryReadContext(payment?.Context, out var context)
            ? PlatformWalletLinkFactory.FromPaymentContext(context)
            : [];
    }

    private async Task<IReadOnlyList<PlatformWalletTransactionLinkInput>> ResolveRefundAsync(
        Guid referenceId,
        long amount,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(new
        {
            allocations = new[] { new { referenceId } },
        });
        var payments = await db.Payments.AsNoTracking()
            .Where(item => (item.Status == PaymentStatus.SUCCEEDED || item.Status == PaymentStatus.REFUNDED)
                && EF.Functions.JsonContains(item.Context, json))
            .OrderByDescending(item => item.SucceededAt)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        var proven = payments
            .Select(item => TryReadContext(item.Context, out var context) ? context : null)
            .Where(context => context?.Allocations.Any(allocation => allocation.ReferenceId == referenceId) == true)
            .ToArray();
        return proven.Length == 1
            ? PlatformWalletLinkFactory.ForRefund(proven[0]!, referenceId, amount)
            : [];
    }

    private async Task<IReadOnlyList<PlatformWalletTransactionLinkInput>> ResolveSubscriptionAsync(
        Guid paymentId,
        long amount,
        CancellationToken cancellationToken)
    {
        var payment = await db.Payments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == paymentId, cancellationToken);
        return payment?.OperatorId is { } operatorId
            ? [new PlatformWalletTransactionLinkInput(
                PlatformWalletTransactionLinkType.SUBSCRIPTION,
                amount,
                operatorId,
                ReferenceId: paymentId)]
            : [];
    }

    private async Task<IReadOnlyList<PlatformWalletTransactionLinkInput>> ResolveSettlementAsync(
        Guid settlementId,
        long amount,
        CancellationToken cancellationToken)
    {
        var settlement = await db.OperatorTripSettlements.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == settlementId, cancellationToken);
        return settlement is null || settlement.NetAmount != amount
            ? []
            : [new PlatformWalletTransactionLinkInput(
                PlatformWalletTransactionLinkType.TRIP_SETTLEMENT,
                amount,
                settlement.OperatorId,
                settlement.TripId,
                settlement.Id,
                settlement.SettlementCode)];
    }

    private async Task<IReadOnlyList<PlatformWalletTransactionLinkInput>> ResolveCompensationAsync(
        Guid claimId,
        long amount,
        CancellationToken cancellationToken)
    {
        var payout = await db.ParcelCompensationPayouts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ClaimId == claimId, cancellationToken);
        return payout is null || payout.AmountVnd != amount
            ? []
            : [new PlatformWalletTransactionLinkInput(
                PlatformWalletTransactionLinkType.PARCEL_CLAIM,
                amount,
                payout.OperatorId,
                payout.TripId,
                payout.ClaimId)];
    }

    private static bool TryReadContext(string? value, out PaymentContextV1 context)
    {
        context = null!;
        if (value is null || PaymentContextCodec.IsMissing(value))
            return false;
        try
        {
            context = PaymentContextCodec.DeserializeTrusted(value);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }
}
