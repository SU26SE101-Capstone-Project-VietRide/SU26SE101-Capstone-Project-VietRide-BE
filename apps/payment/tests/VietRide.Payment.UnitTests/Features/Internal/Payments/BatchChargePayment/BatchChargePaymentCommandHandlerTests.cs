using FluentAssertions;
using VietRide.Payment.Application.Features.Internal.Payments.BatchChargePayment;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Exceptions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.UnitTests.Features.Internal.Payments.BatchChargePayment;

public sealed class BatchChargePaymentCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WhenWalletHasEnoughBalance_CreatesPerBookingPaymentsAndDebitsAtomically()
    {
        var userId = Guid.NewGuid();
        var outboundBookingId = Guid.NewGuid();
        var returnBookingId = Guid.NewGuid();
        var db = new FakeDbContext(new Wallet(userId, Money.FromRaw(250_000)));
        var handler = CreateHandler(db);
        var command = CreateCommand(userId, outboundBookingId, returnBookingId, 80_000, 120_000);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Payments.Should().HaveCount(2);
        result.Payments.Select(x => x.ReferenceType).Should().OnlyContain(x => x == "BOOKING");
        result.Payments.Select(x => x.Status).Should().OnlyContain(x => x == "SUCCEEDED");
        result.Payments.Select(x => x.PaymentRedirectUrl).Should().OnlyContain(x => x == null);
        db.Payments.Should().HaveCount(2);
        db.Payments.Select(x => x.IdempotencyKey).Should().OnlyContain(x => x == null);
        db.WalletTransactions.Should().HaveCount(2);
        db.WalletTransactions.Select(x => x.ReferenceType.ToString()).Should().OnlyContain(x => x == "BOOKING_PAYMENT");
        db.Wallet!.Balance.Amount.Should().Be(50_000);
        db.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenBalanceIsInsufficient_CommitsNoPartialPaymentOrDebit()
    {
        var userId = Guid.NewGuid();
        var db = new FakeDbContext(new Wallet(userId, Money.FromRaw(100_000)));
        var handler = CreateHandler(db);
        var command = CreateCommand(userId, Guid.NewGuid(), Guid.NewGuid(), 80_000, 120_000);

        var ex = await Assert.ThrowsAsync<PaymentInsufficientWalletException>(() => handler.Handle(command, CancellationToken.None));

        ex.ErrorCode.Should().Be("PAYMENT_INSUFFICIENT_WALLET");
        db.Payments.Should().BeEmpty();
        db.WalletTransactions.Should().BeEmpty();
        db.Wallet!.Balance.Amount.Should().Be(100_000);
        db.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenRequestContainsDuplicateReferences_CommitsNothing()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var db = new FakeDbContext(new Wallet(userId, Money.FromRaw(250_000)));
        var handler = CreateHandler(db);
        var command = CreateCommand(userId, bookingId, bookingId, 80_000, 120_000);

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() => handler.Handle(command, CancellationToken.None));

        ex.ErrorCode.Should().Be("VALIDATION_ERROR");
        db.Payments.Should().BeEmpty();
        db.WalletTransactions.Should().BeEmpty();
        db.Wallet!.Balance.Amount.Should().Be(250_000);
        db.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenReferenceWasAlreadyPaid_ReturnsRegisteredConflictCodeAndCommitsNothing()
    {
        var userId = Guid.NewGuid();
        var outboundBookingId = Guid.NewGuid();
        var returnBookingId = Guid.NewGuid();
        var db = new FakeDbContext(new Wallet(userId, Money.FromRaw(250_000)))
        {
            ExistingReferenceId = outboundBookingId,
        };
        var handler = CreateHandler(db);
        var command = CreateCommand(userId, outboundBookingId, returnBookingId, 80_000, 120_000);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));

        ex.ErrorCode.Should().Be("PAYMENT_ALREADY_PROCESSED");
        db.Payments.Should().BeEmpty();
        db.WalletTransactions.Should().BeEmpty();
        db.Wallet!.Balance.Amount.Should().Be(250_000);
        db.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenMethodIsNotWallet_RejectsBeforeMutation()
    {
        var userId = Guid.NewGuid();
        var db = new FakeDbContext(new Wallet(userId, Money.FromRaw(250_000)));
        var handler = CreateHandler(db);
        var command = CreateCommand(userId, Guid.NewGuid(), Guid.NewGuid(), 80_000, 120_000) with
        {
            Method = "VNPAY",
        };

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() => handler.Handle(command, CancellationToken.None));

        ex.ErrorCode.Should().Be("VALIDATION_ERROR");
        db.Payments.Should().BeEmpty();
        db.WalletTransactions.Should().BeEmpty();
        db.Wallet!.Balance.Amount.Should().Be(250_000);
        db.SaveChangesCount.Should().Be(0);
    }

    private static BatchChargePaymentCommandHandler CreateHandler(FakeDbContext db)
    {
        var clock = new FrozenClock(Now);
        return new BatchChargePaymentCommandHandler(db, clock);
    }

    private static BatchChargePaymentCommand CreateCommand(
        Guid userId,
        Guid outboundBookingId,
        Guid returnBookingId,
        long outboundAmount,
        long returnAmount)
        => new(
            userId,
            "WALLET",
            [
                new BatchChargePaymentCommand.Item("BOOKING", outboundBookingId, outboundAmount),
                new BatchChargePaymentCommand.Item("BOOKING", returnBookingId, returnAmount),
            ],
            "idem-key");

    private sealed class FakeDbContext : IBatchChargePaymentDbContext
    {
        public FakeDbContext(Wallet? wallet)
        {
            Wallet = wallet;
        }

        public Wallet? Wallet { get; }
        public Guid? ExistingReferenceId { get; init; }
        public List<PaymentEntity> Payments { get; } = [];
        public List<WalletTransaction> WalletTransactions { get; } = [];
        public int SaveChangesCount { get; private set; }

        public Task<Wallet?> FindWalletAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(Wallet is not null && Wallet.UserId == userId ? Wallet : null);

        public Task AcquirePaymentReferenceLocksAsync(IReadOnlyCollection<BatchChargePaymentCommand.Item> items, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<bool> PaymentReferenceExistsAsync(string referenceType, Guid referenceId, CancellationToken cancellationToken)
            => Task.FromResult(ExistingReferenceId == referenceId);

        public void AddPayment(PaymentEntity payment)
            => Payments.Add(payment);

        public void AddWalletTransaction(WalletTransaction transaction)
            => WalletTransactions.Add(transaction);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
