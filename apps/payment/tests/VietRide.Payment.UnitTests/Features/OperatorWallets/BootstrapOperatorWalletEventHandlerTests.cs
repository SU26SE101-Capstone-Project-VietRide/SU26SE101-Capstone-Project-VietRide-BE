using FluentAssertions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Features.OperatorWallets.BootstrapOperatorWallet;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.UnitTests.Features.OperatorWallets;

public sealed class BootstrapOperatorWalletEventHandlerTests
{
    [Fact]
    public async Task HandleAsync_Replay_CreatesZeroWalletAndProcessedMarkerOnce()
    {
        var wallets = new FakeOperatorWalletRepository();
        var processed = new FakeProcessedEventRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new BootstrapOperatorWalletEventHandler(
            wallets,
            processed,
            unitOfWork,
            new FrozenClock(new DateTimeOffset(2026, 7, 13, 4, 0, 0, TimeSpan.Zero)));
        var evt = new OperatorApprovedConsumerEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        await handler.HandleAsync(evt, CancellationToken.None);
        await handler.HandleAsync(evt, CancellationToken.None);

        wallets.Wallets.Should().ContainSingle(wallet =>
            wallet.OperatorId == evt.OperatorId && wallet.Balance.Amount == 0);
        processed.Events.Should().ContainSingle(marker => marker.EventId == evt.EventId);
        unitOfWork.CommitCount.Should().Be(1);
    }

    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeOperatorWalletRepository : IOperatorWalletRepository
    {
        public List<OperatorWallet> Wallets { get; } = [];

        public Task<OperatorWallet?> FindByOperatorIdAsync(Guid operatorId, CancellationToken cancellationToken)
            => Task.FromResult(Wallets.SingleOrDefault(wallet => wallet.OperatorId == operatorId));

        public Task<OperatorWallet?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => FindByOperatorIdAsync(id, cancellationToken);

        public Task<OperatorWallet> AddAsync(OperatorWallet entity, CancellationToken cancellationToken = default)
        {
            Wallets.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(OperatorWallet entity) => throw new NotSupportedException();
        public void Remove(OperatorWallet entity) => throw new NotSupportedException();
        public IQueryable<OperatorWallet> Query() => Wallets.AsQueryable();
        public IQueryable<OperatorWallet> QueryNoTracking() => Query();
    }

    private sealed class FakeProcessedEventRepository : IProcessedIntegrationEventRepository
    {
        public List<ProcessedIntegrationEvent> Events { get; } = [];

        public Task<bool> ExistsAsync(string consumer, Guid eventId, CancellationToken cancellationToken)
            => Task.FromResult(Events.Any(item => item.Consumer == consumer && item.EventId == eventId));

        public Task<ProcessedIntegrationEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Events.SingleOrDefault(item => item.Id == id));

        public Task<ProcessedIntegrationEvent> AddAsync(ProcessedIntegrationEvent entity, CancellationToken cancellationToken = default)
        {
            Events.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(ProcessedIntegrationEvent entity) => throw new NotSupportedException();
        public void Remove(ProcessedIntegrationEvent entity) => throw new NotSupportedException();
        public IQueryable<ProcessedIntegrationEvent> Query() => Events.AsQueryable();
        public IQueryable<ProcessedIntegrationEvent> QueryNoTracking() => Query();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int CommitCount { get; private set; }
        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);

        public Task CommitAsync(CancellationToken ct)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct) => operation();
    }
}
