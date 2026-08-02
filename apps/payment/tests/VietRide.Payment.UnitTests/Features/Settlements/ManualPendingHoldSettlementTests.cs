using FluentAssertions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Exceptions;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Payment.Application.Features.OperatorReports;
using VietRide.Payment.Application.Features.Settlements.SettleTrip;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Domain.ValueObjects;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.UnitTests.Features.Settlements;

public sealed class ManualPendingHoldSettlementTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 2, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ManualPositiveNet_PendingHold_SettlesBeforeEligibility()
    {
        var fixture = new SettlementFixture(netAmount: 250_000, eligible: false);

        var result = await fixture.CreateService().SettleAsync(
            fixture.Settlement.Id,
            OperatorTripSettlementMethod.ADMIN_MANUAL,
            fixture.Actor,
            conflictWhenAlreadyTerminal: true,
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be(nameof(OperatorTripSettlementStatus.SETTLED));
        fixture.Settlement.SettlementMethod.Should().Be(OperatorTripSettlementMethod.ADMIN_MANUAL);
        fixture.Settlement.SettledByUserId.Should().Be(fixture.Actor.UserId);
        fixture.PlatformWallets.DebitCount.Should().Be(1);
        fixture.OperatorTransactions.AddCount.Should().Be(1);
        fixture.Outbox.EnqueueCount.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25_000)]
    public async Task ManualNonPositiveNet_CancelsMarkerWithoutMoneyMovementOrEvent(long netAmount)
    {
        var fixture = new SettlementFixture(netAmount, eligible: false);

        var result = await fixture.CreateService().SettleAsync(
            fixture.Settlement.Id,
            OperatorTripSettlementMethod.ADMIN_MANUAL,
            fixture.Actor,
            conflictWhenAlreadyTerminal: true,
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be(nameof(OperatorTripSettlementStatus.CANCELLED));
        result.NetAmount.Should().Be(netAmount);
        fixture.Settlement.SettlementMethod.Should().Be(OperatorTripSettlementMethod.ADMIN_MANUAL);
        fixture.Settlement.SettledByUserId.Should().Be(fixture.Actor.UserId);
        fixture.Settlement.WalletTransactionId.Should().BeNull();
        fixture.PlatformWallets.DebitCount.Should().Be(0);
        fixture.OperatorTransactions.AddCount.Should().Be(0);
        fixture.Outbox.EnqueueCount.Should().Be(0);
    }

    [Fact]
    public async Task ManualPositiveNet_Eligible_SettlesNormally()
    {
        var fixture = new SettlementFixture(netAmount: 125_000, eligible: true);

        var result = await fixture.CreateService().SettleAsync(
            fixture.Settlement.Id,
            OperatorTripSettlementMethod.ADMIN_MANUAL,
            fixture.Actor,
            conflictWhenAlreadyTerminal: true,
            CancellationToken.None);

        result.Should().NotBeNull();
        fixture.Settlement.Status.Should().Be(OperatorTripSettlementStatus.SETTLED);
        fixture.Settlement.SettlementMethod.Should().Be(OperatorTripSettlementMethod.ADMIN_MANUAL);
        fixture.PlatformWallets.DebitCount.Should().Be(1);
        fixture.Outbox.EnqueueCount.Should().Be(1);
    }

    [Fact]
    public async Task WeeklyPositiveNet_PendingHold_RemainsExcluded()
    {
        var fixture = new SettlementFixture(netAmount: 125_000, eligible: false);

        var action = () => fixture.CreateService().SettleAsync(
            fixture.Settlement.Id,
            OperatorTripSettlementMethod.AUTO_WEEKLY,
            settledBy: null,
            conflictWhenAlreadyTerminal: false,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_SETTLEMENT_NOT_ELIGIBLE");
        fixture.Settlement.Status.Should().Be(OperatorTripSettlementStatus.PENDING_HOLD);
        fixture.PlatformWallets.DebitCount.Should().Be(0);
        fixture.OperatorTransactions.AddCount.Should().Be(0);
        fixture.Outbox.EnqueueCount.Should().Be(0);
    }

    [Fact]
    public async Task WeeklyZeroNet_PendingHold_RemainsExcluded()
    {
        var fixture = new SettlementFixture(netAmount: 0, eligible: false);

        var action = () => fixture.CreateService().SettleAsync(
            fixture.Settlement.Id,
            OperatorTripSettlementMethod.AUTO_WEEKLY,
            settledBy: null,
            conflictWhenAlreadyTerminal: false,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_SETTLEMENT_NOT_ELIGIBLE");
        fixture.Settlement.Status.Should().Be(OperatorTripSettlementStatus.PENDING_HOLD);
        fixture.Settlement.SettlementMethod.Should().BeNull();
        fixture.PlatformWallets.DebitCount.Should().Be(0);
        fixture.OperatorTransactions.AddCount.Should().Be(0);
        fixture.Outbox.EnqueueCount.Should().Be(0);
    }

    [Fact]
    public async Task WeeklyZeroNet_Eligible_CancelsWithoutMoneyMovementOrEvent()
    {
        var fixture = new SettlementFixture(netAmount: 0, eligible: true);

        var result = await fixture.CreateService().SettleAsync(
            fixture.Settlement.Id,
            OperatorTripSettlementMethod.AUTO_WEEKLY,
            settledBy: null,
            conflictWhenAlreadyTerminal: false,
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be(nameof(OperatorTripSettlementStatus.CANCELLED));
        fixture.Settlement.SettlementMethod.Should().Be(OperatorTripSettlementMethod.AUTO_WEEKLY);
        fixture.PlatformWallets.DebitCount.Should().Be(0);
        fixture.OperatorTransactions.AddCount.Should().Be(0);
        fixture.Outbox.EnqueueCount.Should().Be(0);
    }

    [Fact]
    public async Task ManualPendingHold_InsufficientPlatformWallet_RollsBackWithCodedError()
    {
        var fixture = new SettlementFixture(netAmount: 125_000, eligible: false, insufficientBalance: true);

        var action = () => fixture.CreateService().SettleAsync(
            fixture.Settlement.Id,
            OperatorTripSettlementMethod.ADMIN_MANUAL,
            fixture.Actor,
            conflictWhenAlreadyTerminal: true,
            CancellationToken.None);

        await action.Should().ThrowAsync<PlatformWalletInsufficientBalanceException>();
        fixture.Settlement.Status.Should().Be(OperatorTripSettlementStatus.PENDING_HOLD);
        fixture.Settlement.SettlementFailureCount.Should().Be(0);
        fixture.OperatorWallet.Balance.Amount.Should().Be(0);
        fixture.OperatorTransactions.AddCount.Should().Be(0);
        fixture.Outbox.EnqueueCount.Should().Be(0);
    }

    [Fact]
    public async Task ManualReplay_AfterTerminalMarker_ReturnsConflictWithoutDuplicateSideEffects()
    {
        var fixture = new SettlementFixture(netAmount: 125_000, eligible: false);
        var service = fixture.CreateService();
        await service.SettleAsync(
            fixture.Settlement.Id,
            OperatorTripSettlementMethod.ADMIN_MANUAL,
            fixture.Actor,
            conflictWhenAlreadyTerminal: true,
            CancellationToken.None);

        var replay = () => fixture.CreateService().SettleAsync(
            fixture.Settlement.Id,
            OperatorTripSettlementMethod.ADMIN_MANUAL,
            fixture.Actor,
            conflictWhenAlreadyTerminal: true,
            CancellationToken.None);

        var exception = await replay.Should().ThrowAsync<ConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_SETTLEMENT_ALREADY_SETTLED");
        fixture.PlatformWallets.DebitCount.Should().Be(1);
        fixture.OperatorTransactions.AddCount.Should().Be(1);
        fixture.Outbox.EnqueueCount.Should().Be(1);
    }

    [Fact]
    public async Task ManualAndWeeklyRace_HasOneWinnerAndOneTerminalMarker()
    {
        var fixture = new SettlementFixture(netAmount: 400_000, eligible: true, holdFirstTransaction: true);
        var manualService = fixture.CreateService();
        var weeklyService = fixture.CreateService();

        var manual = CaptureAsync(() => manualService.SettleAsync(
            fixture.Settlement.Id,
            OperatorTripSettlementMethod.ADMIN_MANUAL,
            fixture.Actor,
            conflictWhenAlreadyTerminal: true,
            CancellationToken.None));
        await fixture.Transactions.FirstEntered;

        var weekly = CaptureAsync(() => weeklyService.SettleAsync(
            fixture.Settlement.Id,
            OperatorTripSettlementMethod.AUTO_WEEKLY,
            settledBy: null,
            conflictWhenAlreadyTerminal: false,
            CancellationToken.None));
        await fixture.Transactions.SecondWaiting;
        fixture.Transactions.ReleaseFirst();

        var attempts = await Task.WhenAll(manual, weekly);

        attempts.Count(item => item.Result is not null).Should().Be(1);
        attempts.Count(item => item.Result is null && item.Exception is null
            || item.Exception is ConflictException conflict
                && conflict.ErrorCode == "TRIP_SETTLEMENT_ALREADY_SETTLED").Should().Be(1);
        fixture.Settlement.Status.Should().Be(OperatorTripSettlementStatus.SETTLED);
        fixture.PlatformWallets.DebitCount.Should().Be(1);
        fixture.OperatorWallet.Balance.Amount.Should().Be(400_000);
        fixture.OperatorTransactions.AddCount.Should().Be(1);
        fixture.Outbox.EnqueueCount.Should().Be(1);
    }

    private static async Task<SettlementAttempt> CaptureAsync(
        Func<Task<TripSettlementResult?>> action)
    {
        try
        {
            return new SettlementAttempt(await action(), null);
        }
        catch (Exception exception)
        {
            return new SettlementAttempt(null, exception);
        }
    }

    private sealed record SettlementAttempt(TripSettlementResult? Result, Exception? Exception);

    private sealed class SettlementFixture
    {
        private readonly FakeSettlementRepository _settlements;
        private readonly FakeLedgerRepository _ledger;
        private readonly FakeOperatorWalletRepository _operatorWallets;

        public SettlementFixture(
            long netAmount,
            bool eligible,
            bool insufficientBalance = false,
            bool holdFirstTransaction = false)
        {
            Settlement = OperatorTripSettlement.CreatePending(
                Guid.NewGuid(),
                Guid.NewGuid(),
                eligible ? Now.AddDays(-8) : Now);
            if (eligible)
                Settlement.RefreshEligibility(1, Now);

            Actor = new FinancialActorSnapshot(
                Guid.NewGuid(),
                "Settlement Admin",
                "settlement-admin@vietride.vn",
                "SYSTEM_ADMIN");
            _settlements = new FakeSettlementRepository(Settlement);
            _ledger = new FakeLedgerRepository(netAmount);
            OperatorWallet = VietRide.Payment.Domain.Entities.OperatorWallet.Create(Settlement.OperatorId);
            _operatorWallets = new FakeOperatorWalletRepository(OperatorWallet);
            PlatformWallets.ThrowInsufficientBalance = insufficientBalance;
            Transactions = new FakeTransactionCoordinator(holdFirstTransaction);
        }

        public OperatorTripSettlement Settlement { get; }
        public FinancialActorSnapshot Actor { get; }
        public OperatorWallet OperatorWallet { get; }
        public FakeTransactionCoordinator Transactions { get; }
        public FakePlatformWalletRepository PlatformWallets { get; } = new();
        public FakeOperatorWalletTransactionRepository OperatorTransactions { get; } = new();
        public FakeOutbox Outbox { get; } = new();

        public TripSettlementService CreateService()
            => new(
                _settlements,
                _ledger,
                PlatformWallets,
                _operatorWallets,
                OperatorTransactions,
                Outbox,
                new FakeUnitOfWork(Transactions),
                new FakeActorPrivacyStore(),
                new FrozenClock(Now));
    }

    private abstract class FakeRepository<TEntity, TId>
        where TEntity : class
        where TId : notnull
    {
        public virtual Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct)
            => Task.FromResult<TEntity?>(null);

        public virtual Task<TEntity> AddAsync(TEntity entity, CancellationToken ct)
            => Task.FromResult(entity);

        public virtual void Update(TEntity entity) { }
        public virtual void Remove(TEntity entity) { }
        public virtual IQueryable<TEntity> Query() => Array.Empty<TEntity>().AsQueryable();
        public virtual IQueryable<TEntity> QueryNoTracking() => Query();
    }

    private sealed class FakeSettlementRepository(OperatorTripSettlement settlement)
        : FakeRepository<OperatorTripSettlement, Guid>, IOperatorTripSettlementRepository
    {
        public override Task<OperatorTripSettlement?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(id == settlement.Id ? settlement : null);

        public Task<OperatorTripSettlement?> FindByOperatorTripAsync(
            Guid operatorId,
            Guid tripId,
            CancellationToken cancellationToken)
            => Task.FromResult(settlement.OperatorId == operatorId && settlement.TripId == tripId
                ? settlement
                : null);

        public Task<OperatorTripSettlement?> GetForUpdateAsync(
            Guid settlementId,
            CancellationToken cancellationToken)
            => GetByIdAsync(settlementId, cancellationToken);
    }

    private sealed class FakeLedgerRepository(long netAmount)
        : FakeRepository<OperatorLedgerEntry, Guid>, IOperatorLedgerEntryRepository
    {
        public Task<IReadOnlyList<PlatformLedgerReportItem>> GetPlatformLedgerMetricsAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<OperatorLedgerReportRow> StreamOperatorReportRowsAsync(
            Guid operatorId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            bool refundOnly,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<long> SumTripNetAmountAsync(
            Guid operatorId,
            Guid tripId,
            CancellationToken cancellationToken)
            => Task.FromResult(netAmount);
    }

    private sealed class FakePlatformWalletRepository
        : FakeRepository<PlatformWallet, Guid>, IPlatformWalletRepository
    {
        private int _debitCount;
        public int DebitCount => _debitCount;
        public bool ThrowInsufficientBalance { get; set; }

        public Task<PlatformWallet> GetSingletonAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<PlatformWalletTransaction> CreditAsync(
            Money amount,
            PlatformWalletTransactionRef referenceType,
            Guid? referenceId,
            string? note,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<PlatformWalletTransaction> DebitAsync(
            Money amount,
            PlatformWalletTransactionRef referenceType,
            Guid? referenceId,
            string? note,
            CancellationToken cancellationToken)
        {
            if (ThrowInsufficientBalance)
                throw new InvalidOperationException("Platform wallet balance is insufficient.");

            Interlocked.Increment(ref _debitCount);
            return Task.FromResult(PlatformWalletTransaction.Create(
                PlatformWalletTransactionType.DEBIT,
                amount,
                Money.FromRaw(1_000_000),
                Money.FromRaw(1_000_000 - amount.Amount),
                referenceType,
                referenceId,
                note));
        }
    }

    private sealed class FakeOperatorWalletRepository(OperatorWallet wallet)
        : FakeRepository<OperatorWallet, Guid>, IOperatorWalletRepository
    {
        public Task<OperatorWallet?> FindByOperatorIdAsync(
            Guid operatorId,
            CancellationToken cancellationToken)
            => Task.FromResult(operatorId == wallet.OperatorId ? wallet : null);
    }

    private sealed class FakeOperatorWalletTransactionRepository
        : FakeRepository<OperatorWalletTransaction, Guid>, IOperatorWalletTransactionRepository
    {
        private int _addCount;
        public int AddCount => _addCount;

        public override Task<OperatorWalletTransaction> AddAsync(
            OperatorWalletTransaction entity,
            CancellationToken ct)
        {
            Interlocked.Increment(ref _addCount);
            return Task.FromResult(entity);
        }
    }

    private sealed class FakeOutbox : IIntegrationEventOutbox
    {
        private int _enqueueCount;
        public int EnqueueCount => _enqueueCount;

        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _enqueueCount);
            return Task.CompletedTask;
        }
    }

    public sealed class FakeTransactionCoordinator
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly bool _holdFirstTransaction;
        private readonly TaskCompletionSource _firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _attemptCount;

        public FakeTransactionCoordinator(bool holdFirstTransaction)
        {
            _holdFirstTransaction = holdFirstTransaction;
            if (!holdFirstTransaction)
                _releaseFirst.TrySetResult();
        }

        public Task FirstEntered => _firstEntered.Task;
        public Task SecondWaiting => _secondWaiting.Task;

        public async Task EnterAsync(CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attemptCount);
            if (attempt == 2)
                _secondWaiting.TrySetResult();

            await _gate.WaitAsync(cancellationToken);
            if (attempt == 1)
            {
                _firstEntered.TrySetResult();
                if (_holdFirstTransaction)
                    await _releaseFirst.Task.WaitAsync(cancellationToken);
            }
        }

        public void Exit() => _gate.Release();

        public void ReleaseFirst() => _releaseFirst.TrySetResult();
    }

    private sealed class FakeUnitOfWork(FakeTransactionCoordinator transactions) : IUnitOfWork
    {
        private bool _ownsTransaction;

        public async Task BeginTransactionAsync(CancellationToken ct)
        {
            await transactions.EnterAsync(ct);
            _ownsTransaction = true;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);

        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
        {
            await BeginTransactionAsync(ct);
            try
            {
                var result = await operation();
                await CommitAsync(ct);
                return result;
            }
            catch
            {
                await RollbackAsync(ct);
                throw;
            }
        }

        public Task CommitAsync(CancellationToken ct)
        {
            ReleaseTransaction();
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct)
        {
            ReleaseTransaction();
            return Task.CompletedTask;
        }

        private void ReleaseTransaction()
        {
            if (!_ownsTransaction)
                return;

            _ownsTransaction = false;
            transactions.Exit();
        }
    }

    private sealed class FakeActorPrivacyStore : IFinancialActorPrivacyStore
    {
        public Task<bool> IsDeletedWithLockAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<int> MarkDeletedAndRedactAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
