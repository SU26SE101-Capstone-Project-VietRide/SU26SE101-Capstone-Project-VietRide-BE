using FluentAssertions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Features.OperatorWallets.BootstrapOperatorWallet;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.UnitTests.Features.OperatorWallets;

public sealed class BootstrapOperatorWalletEventHandlerTests
{
    [Fact]
    public async Task HandleAsync_Replay_CreatesZeroWalletOnce()
    {
        var wallets = new FakeOperatorWalletRepository();
        var handler = new BootstrapOperatorWalletEventHandler(wallets);
        var evt = new OperatorApprovedConsumerEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        await handler.HandleAsync(evt, CancellationToken.None);
        await handler.HandleAsync(evt, CancellationToken.None);

        wallets.Wallets.Should().ContainSingle(wallet =>
            wallet.OperatorId == evt.OperatorId && wallet.Balance.Amount == 0);
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

}
