using System.Reflection;
using FluentAssertions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Compensation;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

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
            Throwing<IClock>());

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
}
