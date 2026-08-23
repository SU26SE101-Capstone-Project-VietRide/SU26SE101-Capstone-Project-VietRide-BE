using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.ExternalClients;

namespace VietRide.Payment.Infrastructure.Jobs;

public sealed class PaymentBusinessCodeBackfillJob
{
    public const string RecurringJobId = "payment.business-code-backfill";
    public const int BatchSize = 100;

    private readonly PaymentDbContext db;
    private readonly ITripRevenueAnalyticsClient trips;
    private readonly ILogger<PaymentBusinessCodeBackfillJob> logger;

    public PaymentBusinessCodeBackfillJob(
        PaymentDbContext db,
        ITripRevenueAnalyticsClient trips,
        ILogger<PaymentBusinessCodeBackfillJob> logger)
    {
        this.db = db;
        this.trips = trips;
        this.logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var settlements = await db.OperatorTripSettlements
            .Where(item => item.SettlementCode == null || item.TripCode == null)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Take(BatchSize)
            .ToArrayAsync(cancellationToken);
        var operatorTransactions = await db.OperatorWalletTransactions
            .Where(item => item.TransactionCode == null)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Take(BatchSize)
            .ToArrayAsync(cancellationToken);
        var platformTransactions = await db.PlatformWalletTransactions
            .Where(item => item.TransactionCode == null)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Take(BatchSize)
            .ToArrayAsync(cancellationToken);
        if (settlements.Length == 0 && operatorTransactions.Length == 0 && platformTransactions.Length == 0)
        {
            return;
        }

        var tripCodes = await LoadTripCodesSafeAsync(
            settlements.Where(item => item.TripCode == null).Select(item => item.TripId).Distinct().ToArray(),
            cancellationToken);
        foreach (var settlement in settlements)
        {
            settlement.BackfillBusinessCodes(tripCodes.GetValueOrDefault(settlement.TripId));
        }

        foreach (var transaction in operatorTransactions)
        {
            transaction.BackfillTransactionCode(transaction.CreatedAt);
        }

        foreach (var transaction in platformTransactions)
        {
            transaction.BackfillTransactionCode(transaction.CreatedAt);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Backfilled business codes for {SettlementCount} settlements, {OperatorTransactionCount} operator transactions, and {PlatformTransactionCount} platform transactions.",
            settlements.Length,
            operatorTransactions.Length,
            platformTransactions.Length);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadTripCodesSafeAsync(
        IReadOnlyList<Guid> tripIds,
        CancellationToken cancellationToken)
    {
        if (tripIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        try
        {
            var summaries = await trips.GetTripSummariesAsync(tripIds, cancellationToken);
            return summaries
                .Where(item => item.TripCode is not null)
                .ToDictionary(item => item.TripId, item => item.TripCode!);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Trip business-code backfill is unavailable for {TripCount} Trips.", tripIds.Count);
            return new Dictionary<Guid, string>();
        }
    }

}
