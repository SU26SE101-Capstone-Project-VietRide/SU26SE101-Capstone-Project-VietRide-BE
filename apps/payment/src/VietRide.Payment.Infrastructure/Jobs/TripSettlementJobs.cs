using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Exceptions;
using VietRide.Payment.Application.Features.Settlements.SettleTrip;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Infrastructure.Jobs;

public sealed class TripSettlementEligibilityFlagJob
{
    public const string RecurringJobId = "payment.trip-settlement-eligibility";

    private readonly IOperatorTripSettlementRepository _settlements;
    private readonly IOperatorLedgerEntryRepository _ledger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public TripSettlementEligibilityFlagJob(
        IOperatorTripSettlementRepository settlements,
        IOperatorLedgerEntryRepository ledger,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _settlements = settlements;
        _ledger = ledger;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var ids = await _settlements.QueryNoTracking()
            .Where(settlement =>
                settlement.Status == OperatorTripSettlementStatus.PENDING_HOLD
                && settlement.EligibleAt <= now)
            .OrderBy(settlement => settlement.EligibleAt)
            .Select(settlement => settlement.Id)
            .Take(1_000)
            .ToArrayAsync(cancellationToken);

        foreach (var id in ids)
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var settlement = await _settlements.GetForUpdateAsync(id, cancellationToken);
                if (settlement is not null
                    && settlement.Status == OperatorTripSettlementStatus.PENDING_HOLD)
                {
                    var net = await _ledger.SumTripNetAmountAsync(
                        settlement.OperatorId,
                        settlement.TripId,
                        cancellationToken);
                    settlement.RefreshEligibility(net, now);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                await _unitOfWork.CommitAsync(cancellationToken);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
}

public sealed class TripSettlementWeeklyAutoSettleJob
{
    public const string RecurringJobId = "payment.trip-settlement-weekly";

    private readonly IOperatorTripSettlementRepository _settlements;
    private readonly TripSettlementService _service;
    private readonly ILogger<TripSettlementWeeklyAutoSettleJob> _logger;

    public TripSettlementWeeklyAutoSettleJob(
        IOperatorTripSettlementRepository settlements,
        TripSettlementService service,
        ILogger<TripSettlementWeeklyAutoSettleJob> logger)
    {
        _settlements = settlements;
        _service = service;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var ids = await _settlements.QueryNoTracking()
            .Where(settlement => settlement.Status == OperatorTripSettlementStatus.ELIGIBLE)
            .OrderBy(settlement => settlement.EligibleAt)
            .Select(settlement => settlement.Id)
            .Take(1_000)
            .ToArrayAsync(cancellationToken);

        foreach (var id in ids)
        {
            try
            {
                await _service.SettleAsync(
                    id,
                    OperatorTripSettlementMethod.AUTO_WEEKLY,
                    settledBy: null,
                    conflictWhenAlreadyTerminal: false,
                    cancellationToken);
            }
            catch (PlatformWalletInsufficientBalanceException)
            {
                _logger.LogWarning(
                    "Weekly settlement {SettlementId} remains eligible because PlatformWallet balance is insufficient.",
                    id);
            }
        }
    }
}

public sealed class TripSettlementStuckAlertJob
{
    public const string RecurringJobId = "payment.trip-settlement-stuck-alert";
    private readonly IOperatorTripSettlementRepository _settlements;
    private readonly IConnectionMultiplexer _redis;
    private readonly IClock _clock;
    private readonly ILogger<TripSettlementStuckAlertJob> _logger;

    public TripSettlementStuckAlertJob(
        IOperatorTripSettlementRepository settlements,
        IConnectionMultiplexer redis,
        IClock clock,
        ILogger<TripSettlementStuckAlertJob> logger)
    {
        _settlements = settlements;
        _redis = redis;
        _clock = clock;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var stuck = await _settlements.QueryNoTracking()
            .Where(settlement =>
                settlement.Status == OperatorTripSettlementStatus.ELIGIBLE
                && settlement.ActiveFailureCode != null)
            .OrderBy(settlement => settlement.LastSettlementFailureAt)
            .Take(1_000)
            .ToArrayAsync(cancellationToken);
        var database = _redis.GetDatabase();

        foreach (var settlement in stuck)
        {
            var key = $"payment:settlement_insufficient:{settlement.Id:D}";
            var shouldAlert = await database.StringSetAsync(
                key,
                "1",
                TimeSpan.FromHours(24),
                When.NotExists);
            if (!shouldAlert)
                continue;

            var severity = settlement.SettlementFailureCount >= 3
                || now - settlement.EligibleAt > TimeSpan.FromDays(21)
                ? "HIGH"
                : "WARNING";
            _logger.LogError(
                "Settlement stuck alert. SettlementId={SettlementId}, OperatorId={OperatorId}, TripId={TripId}, FailureCode={FailureCode}, FailureCount={FailureCount}, Severity={Severity}.",
                settlement.Id,
                settlement.OperatorId,
                settlement.TripId,
                settlement.ActiveFailureCode,
                settlement.SettlementFailureCount,
                severity);
        }
    }
}
