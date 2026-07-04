using System.Text.Json;
using FluentAssertions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Exceptions;
using VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.UnitTests.Features.Internal.Wallets.RefundToWallet;

public sealed class RefundToWalletCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenRefundIsValid_DebitsPlatformCreditsWalletAndEnqueuesWalletCredited()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var wallets = new FakeWalletRepository(userId, Money.FromRaw(1_000_000));
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(500_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = new RefundToWalletCommandHandler(wallets, platformWallets, outbox);

        var result = await handler.Handle(CreateCommand(userId, bookingId), CancellationToken.None);

        result.BalanceAfter.Should().Be(1_175_000);
        result.WalletTransactionId.Should().Be(wallets.Transactions.Single().Id);
        wallets.Balance.Amount.Should().Be(1_175_000);
        wallets.Transactions.Should().ContainSingle(x =>
            x.Type == WalletTransactionType.CREDIT
            && x.ReferenceType == WalletTransactionRef.BOOKING_REFUND
            && x.ReferenceId == bookingId);
        wallets.AcquiredReferenceLocks.Should().ContainSingle(x =>
            x.ReferenceType == WalletTransactionRef.BOOKING_REFUND && x.ReferenceId == bookingId);
        platformWallets.Balance.Amount.Should().Be(325_000);
        platformWallets.Transactions.Should().ContainSingle(x =>
            x.Type == PlatformWalletTransactionType.DEBIT
            && x.ReferenceType == PlatformWalletTransactionRef.BOOKING_REFUND
            && x.ReferenceId == bookingId);
        outbox.Events.Should().ContainSingle(evt => evt.EventType == "payment.wallet.credited");
        using var payload = JsonDocument.Parse(outbox.Events.Single().PayloadJson);
        payload.RootElement.GetProperty("userId").GetGuid().Should().Be(userId);
        payload.RootElement.GetProperty("amount").GetInt64().Should().Be(175_000);
        payload.RootElement.GetProperty("referenceType").GetString().Should().Be("BOOKING_REFUND");
        payload.RootElement.GetProperty("referenceId").GetGuid().Should().Be(bookingId);
    }

    [Fact]
    public async Task Handle_WhenParcelRefundIsValid_UsesParcelRefundReferences()
    {
        var userId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        var wallets = new FakeWalletRepository(userId, Money.FromRaw(1_000_000));
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(500_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = new RefundToWalletCommandHandler(wallets, platformWallets, outbox);

        var result = await handler.Handle(
            CreateCommand(userId, parcelId, referenceType: "PARCEL_REFUND"),
            CancellationToken.None);

        result.BalanceAfter.Should().Be(1_175_000);
        wallets.Transactions.Should().ContainSingle(x =>
            x.Type == WalletTransactionType.CREDIT
            && x.ReferenceType == WalletTransactionRef.PARCEL_REFUND
            && x.ReferenceId == parcelId);
        wallets.AcquiredReferenceLocks.Should().ContainSingle(x =>
            x.ReferenceType == WalletTransactionRef.PARCEL_REFUND && x.ReferenceId == parcelId);
        platformWallets.Transactions.Should().ContainSingle(x =>
            x.Type == PlatformWalletTransactionType.DEBIT
            && x.ReferenceType == PlatformWalletTransactionRef.PARCEL_REFUND
            && x.ReferenceId == parcelId);
        outbox.Events.Should().ContainSingle(evt => evt.EventType == "payment.wallet.credited");
        using var payload = JsonDocument.Parse(outbox.Events.Single().PayloadJson);
        payload.RootElement.GetProperty("referenceType").GetString().Should().Be("PARCEL_REFUND");
        payload.RootElement.GetProperty("referenceId").GetGuid().Should().Be(parcelId);
    }

    [Fact]
    public async Task Handle_WhenReferenceWasAlreadyRefunded_ReturnsExistingTransactionWithoutDoubleCredit()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var wallets = new FakeWalletRepository(userId, Money.FromRaw(1_000_000));
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(500_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = new RefundToWalletCommandHandler(wallets, platformWallets, outbox);
        var first = await handler.Handle(CreateCommand(userId, bookingId), CancellationToken.None);

        var second = await handler.Handle(CreateCommand(userId, bookingId, idempotencyKey: "different-key"), CancellationToken.None);

        second.Should().Be(first);
        wallets.Transactions.Should().ContainSingle();
        wallets.Balance.Amount.Should().Be(1_175_000);
        platformWallets.Transactions.Should().ContainSingle();
        platformWallets.Balance.Amount.Should().Be(325_000);
        outbox.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_WhenPlatformWalletWouldUnderflow_ThrowsRegisteredErrorWithoutWalletCreditOrOutbox()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var wallets = new FakeWalletRepository(userId, Money.FromRaw(1_000_000));
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(100_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = new RefundToWalletCommandHandler(wallets, platformWallets, outbox);

        var act = async () => await handler.Handle(CreateCommand(userId, bookingId), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<PlatformWalletInsufficientBalanceException>();
        exception.Which.ErrorCode.Should().Be("PLATFORM_WALLET_INSUFFICIENT_BALANCE");
        wallets.Transactions.Should().BeEmpty();
        wallets.Balance.Amount.Should().Be(1_000_000);
        platformWallets.Transactions.Should().BeEmpty();
        platformWallets.Balance.Amount.Should().Be(100_000);
        outbox.Events.Should().BeEmpty();
    }

    private static RefundToWalletCommand CreateCommand(
        Guid userId,
        Guid bookingId,
        string idempotencyKey = "idem-key",
        string referenceType = "BOOKING_REFUND")
        => new(userId, 175_000, referenceType, bookingId, idempotencyKey);

    private sealed class FakeWalletRepository : IWalletRepository
    {
        private readonly Guid _userId;
        private readonly List<WalletTransaction> _transactions = [];

        public FakeWalletRepository(Guid userId, Money balance)
        {
            _userId = userId;
            Balance = balance;
        }

        public Money Balance { get; private set; }
        public IReadOnlyList<WalletTransaction> Transactions => _transactions;
        public List<(WalletTransactionRef ReferenceType, Guid ReferenceId)> AcquiredReferenceLocks { get; } = [];

        public Task<Wallet?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<Wallet?>(null);

        public Task<Wallet> AddAsync(Wallet entity, CancellationToken ct)
            => Task.FromResult(entity);

        public void Update(Wallet entity)
        {
        }

        public void Remove(Wallet entity)
        {
        }

        public IQueryable<Wallet> Query()
            => Array.Empty<Wallet>().AsQueryable();

        public IQueryable<Wallet> QueryNoTracking()
            => Array.Empty<Wallet>().AsQueryable();

        public Task<bool> EnsureBootstrapWalletAsync(Guid userId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AcquireWalletTransactionReferenceLockAsync(
            WalletTransactionRef referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
        {
            AcquiredReferenceLocks.Add((referenceType, referenceId));
            return Task.CompletedTask;
        }

        public Task<WalletTransaction?> FindTransactionByReferenceAsync(
            WalletTransactionRef referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
            => Task.FromResult(_transactions.FirstOrDefault(transaction =>
                transaction.ReferenceType == referenceType && transaction.ReferenceId == referenceId));

        public Task<WalletTransaction> CreditBookingRefundAsync(
            Guid userId,
            Money amount,
            Guid bookingId,
            CancellationToken cancellationToken)
            => CreditRefundAsync(userId, amount, WalletTransactionRef.BOOKING_REFUND, bookingId, cancellationToken);

        public Task<WalletTransaction> CreditRefundAsync(
            Guid userId,
            Money amount,
            WalletTransactionRef referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
        {
            userId.Should().Be(_userId);
            var before = Balance;
            Balance += amount;
            var transaction = WalletTransaction.CreateRefundCredit(userId, referenceType, referenceId, amount, before, Balance);
            _transactions.Add(transaction);
            return Task.FromResult(transaction);
        }
    }

    private sealed class FakePlatformWalletRepository : IPlatformWalletRepository
    {
        private readonly List<PlatformWalletTransaction> _transactions = [];

        public FakePlatformWalletRepository(Money balance)
        {
            Balance = balance;
        }

        public Money Balance { get; private set; }
        public IReadOnlyList<PlatformWalletTransaction> Transactions => _transactions;

        public Task<PlatformWallet?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<PlatformWallet?>(null);

        public Task<PlatformWallet> AddAsync(PlatformWallet entity, CancellationToken ct)
            => Task.FromResult(entity);

        public void Update(PlatformWallet entity)
        {
        }

        public void Remove(PlatformWallet entity)
        {
        }

        public IQueryable<PlatformWallet> Query()
            => Array.Empty<PlatformWallet>().AsQueryable();

        public IQueryable<PlatformWallet> QueryNoTracking()
            => Array.Empty<PlatformWallet>().AsQueryable();

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
            if (Balance < amount)
            {
                throw new InvalidOperationException("Platform wallet balance cannot be negative.");
            }

            var before = Balance;
            Balance -= amount;
            var transaction = PlatformWalletTransaction.Create(
                PlatformWalletTransactionType.DEBIT,
                amount,
                before,
                Balance,
                referenceType,
                referenceId,
                note);
            _transactions.Add(transaction);
            return Task.FromResult(transaction);
        }
    }

    private sealed class FakeIntegrationEventOutbox : IIntegrationEventOutbox
    {
        public List<(string EventType, string PayloadJson)> Events { get; } = [];

        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
        {
            Events.Add((eventType, payloadJson));
            return Task.CompletedTask;
        }
    }
}
