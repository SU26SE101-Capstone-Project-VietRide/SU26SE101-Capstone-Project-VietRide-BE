using FluentAssertions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Wallets.GetWallet;
using VietRide.Payment.Application.Features.Wallets.GetWalletTransactions;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.UnitTests.Features.Wallets.GetWallet;

public sealed class GetWalletQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenWalletExists_ReturnsAuthenticatedUsersWallet()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var wallet = Wallet.Create(userId);
        wallet.Credit(Money.FromRaw(125_000));
        var repository = new FakeWalletRepository(wallet, Wallet.Create(otherUserId));
        var handler = new GetWalletQueryHandler(repository);

        var result = await handler.Handle(new GetWalletQuery(userId), CancellationToken.None);

        result.UserId.Should().Be(userId);
        result.Balance.Should().Be(125_000);
        result.Currency.Should().Be("VND");
        repository.LastWalletUserId.Should().Be(userId);
    }

    [Fact]
    public async Task Handle_WhenWalletIsMissing_ThrowsNotFoundException()
    {
        var userId = Guid.NewGuid();
        var repository = new FakeWalletRepository();
        var handler = new GetWalletQueryHandler(repository);

        var act = async () => await handler.Handle(new GetWalletQuery(userId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private sealed class FakeWalletRepository : IWalletRepository
    {
        private readonly Dictionary<Guid, Wallet> _wallets;

        public FakeWalletRepository(params Wallet[] wallets)
        {
            _wallets = wallets.ToDictionary(wallet => wallet.UserId);
        }

        public Guid? LastWalletUserId { get; private set; }

        public Task<Wallet?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_wallets.TryGetValue(id, out var wallet) ? wallet : null);

        public Task<Wallet> AddAsync(Wallet entity, CancellationToken ct)
        {
            _wallets[entity.UserId] = entity;
            return Task.FromResult(entity);
        }

        public void Update(Wallet entity)
            => _wallets[entity.UserId] = entity;

        public void Remove(Wallet entity)
            => _wallets.Remove(entity.UserId);

        public IQueryable<Wallet> Query()
            => _wallets.Values.AsQueryable();

        public IQueryable<Wallet> QueryNoTracking()
            => _wallets.Values.AsQueryable();

        public Task<bool> EnsureBootstrapWalletAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<Wallet?> GetUserWalletAsync(Guid userId, CancellationToken cancellationToken)
        {
            LastWalletUserId = userId;
            return Task.FromResult(_wallets.TryGetValue(userId, out var wallet) ? wallet : null);
        }

        public Task<PagedResult<GetWalletTransactionResult>> GetUserWalletTransactionsAsync(
            Guid userId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            WalletTransactionType? type,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Get wallet tests do not read transactions.");

        public Task<WalletTransaction> CreditTopUpAsync(
            Guid userId,
            Money amount,
            Guid topUpRequestId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Get wallet tests do not credit top-ups.");
    }
}
