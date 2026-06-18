using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Features.Wallets.BootstrapWallet;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.UnitTests.Features.Wallets.BootstrapWallet;

public sealed class BootstrapWalletCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenWalletIsMissing_CreatesZeroBalanceVndWallet()
    {
        var repository = new FakeWalletRepository();
        var handler = new BootstrapWalletCommandHandler(repository, NullLogger<BootstrapWalletCommandHandler>.Instance);
        var eventPayload = new UserCreatedIntegrationEvent(
            Guid.NewGuid(),
            "PASSENGER",
            "user@example.com",
            DateTimeOffset.UtcNow);

        await handler.HandleAsync(eventPayload, CancellationToken.None);

        repository.Wallets.Should().ContainSingle();
        var wallet = repository.Wallets.Values.Single();
        wallet.UserId.Should().Be(eventPayload.UserId);
        wallet.Balance.Should().Be(Money.Zero);
        wallet.Currency.Should().Be("VND");
        repository.InsertCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_WhenRedelivered_IsIdempotent()
    {
        var repository = new FakeWalletRepository();
        var handler = new BootstrapWalletCommandHandler(repository, NullLogger<BootstrapWalletCommandHandler>.Instance);
        var eventPayload = new UserCreatedIntegrationEvent(
            Guid.NewGuid(),
            "PASSENGER",
            "user@example.com",
            DateTimeOffset.UtcNow);

        await handler.HandleAsync(eventPayload, CancellationToken.None);
        await handler.HandleAsync(eventPayload, CancellationToken.None);

        repository.Wallets.Should().ContainSingle();
        repository.InsertCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_WhenUserIdIsEmpty_ThrowsArgumentException()
    {
        var repository = new FakeWalletRepository();
        var handler = new BootstrapWalletCommandHandler(repository, NullLogger<BootstrapWalletCommandHandler>.Instance);
        var eventPayload = new UserCreatedIntegrationEvent(
            Guid.Empty,
            "PASSENGER",
            "user@example.com",
            DateTimeOffset.UtcNow);

        var act = async () => await handler.HandleAsync(eventPayload, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("request");
        repository.InsertCount.Should().Be(0);
    }

    private sealed class FakeWalletRepository : IWalletRepository
    {
        public Dictionary<Guid, Wallet> Wallets { get; } = new();

        public int InsertCount { get; private set; }

        public Task<Wallet?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Wallets.TryGetValue(id, out var wallet) ? wallet : null);

        public Task<Wallet> AddAsync(Wallet entity, CancellationToken ct)
        {
            Wallets[entity.UserId] = entity;
            return Task.FromResult(entity);
        }

        public void Update(Wallet entity)
            => Wallets[entity.UserId] = entity;

        public void Remove(Wallet entity)
            => Wallets.Remove(entity.UserId);

        public IQueryable<Wallet> Query()
            => Wallets.Values.AsQueryable();

        public IQueryable<Wallet> QueryNoTracking()
            => Wallets.Values.AsQueryable();

        public Task<bool> EnsureBootstrapWalletAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("User id cannot be empty.", nameof(userId));

            if (Wallets.ContainsKey(userId))
                return Task.FromResult(false);

            Wallets[userId] = Wallet.Create(userId);
            InsertCount++;
            return Task.FromResult(true);
        }
    }
}
