using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Compensation;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.UnitTests.Features.Compensation;

public sealed class ParcelCompensationReplayTests
{
    [Fact]
    public async Task PaidCompensationReplay_DoesNotCreateAnotherPayoutWalletTransactionOrOutbox()
    {
        var claimId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var beneficiaryId = Guid.NewGuid();
        var payout = ParcelCompensationPayout.Create(
            claimId,
            parcelId,
            tripId,
            operatorId,
            beneficiaryId,
            300_000);
        payout.MarkPaid(
            ParcelCompensationFundingSource.OPERATOR_WALLET,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        payout.MarkPaidEventEnqueued(Guid.NewGuid());
        var payouts = new FakePayoutRepository(payout);
        var service = new ParcelCompensationPayoutService(
            payouts,
            Throwing<IWalletRepository>(),
            Throwing<IPlatformWalletRepository>(),
            Throwing<IOperatorWalletRepository>(),
            Throwing<IOperatorWalletTransactionRepository>(),
            Throwing<IOperatorLedgerEntryRepository>(),
            Throwing<IOperatorTripSettlementRepository>(),
            Throwing<IIntegrationEventOutbox>(),
            new ImmediateUnitOfWork(),
            Throwing<IClock>(),
            NullLogger<ParcelCompensationPayoutService>.Instance);

        await service.ProcessApprovedClaimAsync(
            Guid.NewGuid(),
            claimId,
            parcelId,
            tripId,
            operatorId,
            beneficiaryId,
            300_000,
            CancellationToken.None);
        await service.ProcessApprovedClaimAsync(
            Guid.NewGuid(),
            claimId,
            parcelId,
            tripId,
            operatorId,
            beneficiaryId,
            300_000,
            CancellationToken.None);

        payouts.FindCount.Should().Be(2);
        payouts.AddCount.Should().Be(0);
        payout.Status.Should().Be(ParcelCompensationPayoutStatus.PAID);
    }

    [Fact]
    public async Task IncompletePaidCompensationReplay_RepairsLedgerAndPaidEventWithoutCreditingAgain()
    {
        var sourceEventId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var beneficiaryId = Guid.NewGuid();
        var amount = Money.FromRaw(300_000);
        var passengerTransaction = WalletTransaction.CreateRefundCredit(
            beneficiaryId,
            WalletTransactionRef.PARCEL_COMPENSATION,
            claimId,
            amount,
            Money.Zero,
            amount);
        var operatorTransaction = OperatorWalletTransaction.Create(
            operatorId,
            OperatorWalletTransactionType.DEBIT,
            amount,
            Money.FromRaw(1_000_000),
            Money.FromRaw(700_000),
            OperatorWalletTransactionRef.PARCEL_COMPENSATION,
            claimId);
        var payout = ParcelCompensationPayout.Create(
            claimId,
            parcelId,
            tripId,
            operatorId,
            beneficiaryId,
            amount.Amount,
            sourceEventId);
        payout.MarkPaid(
            ParcelCompensationFundingSource.OPERATOR_WALLET,
            passengerTransaction.Id,
            DateTimeOffset.UtcNow);
        var wallets = new FakeWalletRepository(passengerTransaction);
        var ledger = new FakeLedgerRepository();
        var outbox = new FakeOutbox();
        var service = new ParcelCompensationPayoutService(
            new FakePayoutRepository(payout),
            wallets,
            new FakePlatformWalletRepository(),
            Throwing<IOperatorWalletRepository>(),
            new FakeOperatorTransactionRepository(operatorTransaction),
            ledger,
            Throwing<IOperatorTripSettlementRepository>(),
            outbox,
            new ImmediateUnitOfWork(),
            new FixedClock(),
            NullLogger<ParcelCompensationPayoutService>.Instance);

        await service.ProcessApprovedClaimAsync(
            sourceEventId,
            claimId,
            parcelId,
            tripId,
            operatorId,
            beneficiaryId,
            amount.Amount,
            CancellationToken.None);
        await service.ProcessApprovedClaimAsync(
            sourceEventId,
            claimId,
            parcelId,
            tripId,
            operatorId,
            beneficiaryId,
            amount.Amount,
            CancellationToken.None);

        wallets.CreditCount.Should().Be(0);
        ledger.Added.Should().ContainSingle();
        ledger.Added[0].Amount.Should().Be(-amount.Amount);
        outbox.Events.Should().ContainSingle(item => item.EventType == ParcelCompensationPayoutService.PaidEventType);
        payout.PaidEventId.Should().NotBeNull();
        payout.SourceEventId.Should().Be(sourceEventId);
    }

    private static T Throwing<T>()
        where T : class
        => DispatchProxy.Create<T, ThrowingDispatchProxy>();

    public class ThrowingDispatchProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new InvalidOperationException(
                $"Unexpected replay side effect: {targetMethod?.DeclaringType?.Name}.{targetMethod?.Name}");
    }

    private sealed class FakePayoutRepository(ParcelCompensationPayout payout)
        : IParcelCompensationPayoutRepository
    {
        public int FindCount { get; private set; }
        public int AddCount { get; private set; }

        public Task<ParcelCompensationPayout?> FindByClaimIdAsync(
            Guid claimId,
            CancellationToken cancellationToken)
        {
            FindCount++;
            return Task.FromResult<ParcelCompensationPayout?>(
                claimId == payout.ClaimId ? payout : null);
        }

        public Task<ParcelCompensationPayout?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<ParcelCompensationPayout?>(id == payout.Id ? payout : null);

        public Task<ParcelCompensationPayout> AddAsync(
            ParcelCompensationPayout entity,
            CancellationToken ct)
        {
            AddCount++;
            return Task.FromResult(entity);
        }

        public void Update(ParcelCompensationPayout entity)
        {
        }

        public void Remove(ParcelCompensationPayout entity)
            => throw new NotSupportedException();

        public IQueryable<ParcelCompensationPayout> Query()
            => new[] { payout }.AsQueryable();

        public IQueryable<ParcelCompensationPayout> QueryNoTracking()
            => new[] { payout }.AsQueryable();
    }

    private sealed class ImmediateUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
            => operation();

        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;

        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeWalletRepository(WalletTransaction transaction) : IWalletRepository
    {
        public int CreditCount { get; private set; }

        public Task AcquireWalletTransactionReferenceLockAsync(
            WalletTransactionRef referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WalletTransaction?> FindTransactionByReferenceAsync(
            WalletTransactionRef referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
            => Task.FromResult<WalletTransaction?>(transaction);

        public Task<WalletTransaction> CreditRefundAsync(
            Guid userId,
            Money amount,
            WalletTransactionRef referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
        {
            CreditCount++;
            return Task.FromResult(transaction);
        }

        public Task<bool> EnsureBootstrapWalletAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(false);
        public Task<Wallet?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Wallet> AddAsync(Wallet entity, CancellationToken ct) => throw new NotSupportedException();
        public void Update(Wallet entity) => throw new NotSupportedException();
        public void Remove(Wallet entity) => throw new NotSupportedException();
        public IQueryable<Wallet> Query() => throw new NotSupportedException();
        public IQueryable<Wallet> QueryNoTracking() => throw new NotSupportedException();
    }

    private sealed class FakePlatformWalletRepository : IPlatformWalletRepository
    {
        public Task<PlatformWalletTransaction?> FindTransactionByReferenceAsync(
            PlatformWalletTransactionRef referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
            => Task.FromResult<PlatformWalletTransaction?>(null);
        public Task<PlatformWalletTransaction> CreditAsync(
            Money amount,
            PlatformWalletTransactionRef referenceType,
            Guid? referenceId,
            string? note,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PlatformWalletTransaction> DebitAsync(
            Money amount,
            PlatformWalletTransactionRef referenceType,
            Guid? referenceId,
            string? note,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PlatformWallet> GetSingletonAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PlatformWallet?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<PlatformWallet> AddAsync(PlatformWallet entity, CancellationToken ct) => throw new NotSupportedException();
        public void Update(PlatformWallet entity) => throw new NotSupportedException();
        public void Remove(PlatformWallet entity) => throw new NotSupportedException();
        public IQueryable<PlatformWallet> Query() => throw new NotSupportedException();
        public IQueryable<PlatformWallet> QueryNoTracking() => throw new NotSupportedException();
    }

    private sealed class FakeOperatorTransactionRepository(OperatorWalletTransaction transaction)
        : IOperatorWalletTransactionRepository
    {
        public Task<OperatorWalletTransaction?> FindByReferenceAsync(
            Guid operatorId,
            OperatorWalletTransactionRef referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
            => Task.FromResult<OperatorWalletTransaction?>(transaction);
        public Task<OperatorWalletTransaction?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<OperatorWalletTransaction> AddAsync(OperatorWalletTransaction entity, CancellationToken ct) => throw new NotSupportedException();
        public void Update(OperatorWalletTransaction entity) => throw new NotSupportedException();
        public void Remove(OperatorWalletTransaction entity) => throw new NotSupportedException();
        public IQueryable<OperatorWalletTransaction> Query() => throw new NotSupportedException();
        public IQueryable<OperatorWalletTransaction> QueryNoTracking() => throw new NotSupportedException();
    }

    private sealed class FakeLedgerRepository : IOperatorLedgerEntryRepository
    {
        public List<OperatorLedgerEntry> Added { get; } = [];
        public Task<bool> HasSourceEntryAsync(Guid sourceEventId, Guid referenceId, CancellationToken cancellationToken)
            => Task.FromResult(false);
        public Task<OperatorLedgerEntry> AddAsync(OperatorLedgerEntry entity, CancellationToken ct)
        {
            Added.Add(entity);
            return Task.FromResult(entity);
        }
        public Task<long> SumTripNetAmountAsync(Guid operatorId, Guid tripId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public Task<OperatorLedgerEntry?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public void Update(OperatorLedgerEntry entity) => throw new NotSupportedException();
        public void Remove(OperatorLedgerEntry entity) => throw new NotSupportedException();
        public IQueryable<OperatorLedgerEntry> Query() => throw new NotSupportedException();
        public IQueryable<OperatorLedgerEntry> QueryNoTracking() => throw new NotSupportedException();
    }

    private sealed class FakeOutbox : IIntegrationEventOutbox
    {
        public List<(Guid EventId, string EventType)> Events { get; } = [];
        public Task EnqueueAsync(Guid eventId, string eventType, string payloadJson, CancellationToken ct = default)
        {
            Events.Add((eventId, eventType));
            return Task.CompletedTask;
        }
        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
    }
}
