using System.Text.Json;
using FluentAssertions;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Domain.Exceptions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.UnitTests.Features.Internal.Payments.ChargePayment;

public sealed class ChargePaymentCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WhenWalletChargeIsValid_DebitsWalletCreditsPlatformAddsPaymentAndOutbox()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var payments = new FakePaymentRepository(userId, Money.FromRaw(350_000));
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(1_000_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(payments, platformWallets, outbox: outbox);

        var result = await handler.Handle(CreateCommand(userId, bookingId, "WALLET", 250_000), CancellationToken.None);

        result.Status.Should().Be("SUCCEEDED");
        result.PaymentRedirectUrl.Should().BeNull();
        payments.Payments.Should().ContainSingle();
        payments.Payments.Single().Id.Should().Be(result.PaymentId);
        payments.Payments.Single().IdempotencyKey.Should().Be("idem-key");
        payments.WalletBalance.Amount.Should().Be(100_000);
        payments.WalletTransactions.Should().ContainSingle(x => x.ReferenceType == WalletTransactionRef.BOOKING_PAYMENT);
        platformWallets.Balance.Amount.Should().Be(1_250_000);
        platformWallets.Transactions.Should().ContainSingle(x => x.ReferenceType == PlatformWalletTransactionRef.BOOKING_PAYMENT_HOLD);
        outbox.Events.Should().ContainSingle(evt => evt.EventType == "payment.payment.succeeded");
        using var payload = JsonDocument.Parse(outbox.Events.Single().PayloadJson);
        payload.RootElement.GetProperty("paymentId").GetGuid().Should().Be(result.PaymentId);
        payload.RootElement.GetProperty("referenceType").GetString().Should().Be("BOOKING");
        payload.RootElement.GetProperty("referenceId").GetGuid().Should().Be(bookingId);
        payload.RootElement.GetProperty("amount").GetInt64().Should().Be(250_000);
    }

    [Fact]
    public async Task Handle_WhenReferenceAlreadyHasPayment_ReturnsConflictAndDoesNotEnqueue()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var existing = PaymentEntity.CreatePendingRedirect(
            PaymentReferenceType.BOOKING,
            bookingId,
            Money.FromRaw(250_000),
            PaymentMethod.WALLET,
            userId: userId,
            idempotencyKey: "different-key");
        existing.MarkSucceeded(null, Now);
        var payments = new FakePaymentRepository(userId, Money.FromRaw(350_000), existing);
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(1_000_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(payments, platformWallets, outbox: outbox);

        var act = async () => await handler.Handle(CreateCommand(userId, bookingId, "WALLET", 250_000), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ConflictException>();
        exception.Which.ErrorCode.Should().Be("PAYMENT_ALREADY_PROCESSED");
        payments.AcquiredReferenceLocks.Should().ContainSingle(x =>
            x.ReferenceType == PaymentReferenceType.BOOKING && x.ReferenceId == bookingId);
        payments.Payments.Should().ContainSingle();
        payments.WalletBalance.Amount.Should().Be(350_000);
        payments.WalletTransactions.Should().BeEmpty();
        platformWallets.Balance.Amount.Should().Be(1_000_000);
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenSecondSameReferenceUsesDifferentIdempotencyKey_ReturnsConflictWithoutDoubleDebitOrOutbox()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var payments = new FakePaymentRepository(userId, Money.FromRaw(600_000));
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(1_000_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(payments, platformWallets, outbox: outbox);

        var first = await handler.Handle(
            CreateCommand(userId, bookingId, "WALLET", 250_000, "first-key"),
            CancellationToken.None);
        var act = async () => await handler.Handle(
            CreateCommand(userId, bookingId, "WALLET", 250_000, "second-key"),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ConflictException>();
        exception.Which.ErrorCode.Should().Be("PAYMENT_ALREADY_PROCESSED");
        first.Status.Should().Be("SUCCEEDED");
        payments.AcquiredReferenceLocks.Should().HaveCount(2);
        payments.Operations.Should().ContainInOrder(
            "lock:BOOKING",
            "find-reference:BOOKING");
        payments.Payments.Should().ContainSingle();
        payments.WalletBalance.Amount.Should().Be(350_000);
        payments.WalletTransactions.Should().ContainSingle();
        platformWallets.Balance.Amount.Should().Be(1_250_000);
        platformWallets.Transactions.Should().ContainSingle();
        outbox.Events.Should().ContainSingle(evt => evt.EventType == "payment.payment.succeeded");
    }

    [Fact]
    public async Task Handle_WhenIdempotencyKeyWasSeen_ReturnsExistingPaymentWithoutDoubleDebitOrOutbox()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var existing = PaymentEntity.CreatePendingRedirect(
            PaymentReferenceType.BOOKING,
            bookingId,
            Money.FromRaw(250_000),
            PaymentMethod.WALLET,
            userId: userId,
            idempotencyKey: "idem-key");
        existing.MarkSucceeded(null, Now);
        var payments = new FakePaymentRepository(userId, Money.FromRaw(350_000), existing);
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(1_000_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(payments, platformWallets, outbox: outbox);

        var result = await handler.Handle(CreateCommand(userId, bookingId, "WALLET", 250_000), CancellationToken.None);

        result.PaymentId.Should().Be(existing.Id);
        result.Status.Should().Be("SUCCEEDED");
        payments.WalletBalance.Amount.Should().Be(350_000);
        payments.WalletTransactions.Should().BeEmpty();
        platformWallets.Transactions.Should().BeEmpty();
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenWalletBalanceIsInsufficient_DoesNotCreatePaymentOrOutbox()
    {
        var userId = Guid.NewGuid();
        var payments = new FakePaymentRepository(userId, Money.FromRaw(100_000));
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(1_000_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(payments, platformWallets, outbox: outbox);

        var act = async () => await handler.Handle(CreateCommand(userId, Guid.NewGuid(), "WALLET", 250_000), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<PaymentInsufficientWalletException>();
        exception.Which.ErrorCode.Should().Be("PAYMENT_INSUFFICIENT_WALLET");
        payments.Payments.Should().BeEmpty();
        payments.WalletTransactions.Should().BeEmpty();
        platformWallets.Transactions.Should().BeEmpty();
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenVnPayChargeIsValid_AddsPendingPaymentOnly()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var payments = new FakePaymentRepository(userId, Money.FromRaw(350_000));
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(1_000_000));
        var vnPay = new FakeVnPayClient("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_TxnRef=fake");
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(payments, platformWallets, vnPay, outbox);

        var result = await handler.Handle(CreateCommand(userId, bookingId, "VNPAY", 250_000), CancellationToken.None);

        result.Status.Should().Be("PENDING_REDIRECT");
        result.PaymentRedirectUrl.Should().Be(vnPay.RedirectUrl);
        payments.Payments.Should().ContainSingle();
        payments.Payments.Single().Method.Should().Be(PaymentMethod.VNPAY);
        payments.WalletBalance.Amount.Should().Be(350_000);
        payments.WalletTransactions.Should().BeEmpty();
        platformWallets.Transactions.Should().BeEmpty();
        outbox.Events.Should().BeEmpty();
    }

    private static ChargePaymentCommandHandler CreateHandler(
        FakePaymentRepository payments,
        FakePlatformWalletRepository platformWallets,
        FakeVnPayClient? vnPay = null,
        FakeIntegrationEventOutbox? outbox = null)
        => new(
            payments,
            platformWallets,
            vnPay ?? new FakeVnPayClient("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html"),
            outbox ?? new FakeIntegrationEventOutbox(),
            new NoOpRevenueLedgerWriter(),
            new FrozenClock(Now));

    private static ChargePaymentCommand CreateCommand(
        Guid userId,
        Guid bookingId,
        string method,
        long amount,
        string idempotencyKey = "idem-key")
        => new(
            "BOOKING",
            bookingId,
            userId,
            amount,
            method,
            new PaymentContextV1(1,
            [
                new PaymentAllocationV1(
                    bookingId,
                    "BOOKING",
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    amount,
                    0,
                    0),
            ]),
            idempotencyKey,
            "203.0.113.10");

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly Guid _userId;
        private readonly List<PaymentEntity> _payments = [];

        public FakePaymentRepository(Guid userId, Money walletBalance, params PaymentEntity[] payments)
        {
            _userId = userId;
            WalletBalance = walletBalance;
            _payments.AddRange(payments);
        }

        public Money WalletBalance { get; private set; }
        public IReadOnlyList<PaymentEntity> Payments => _payments;
        public List<WalletTransaction> WalletTransactions { get; } = [];
        public List<(PaymentReferenceType ReferenceType, Guid ReferenceId)> AcquiredReferenceLocks { get; } = [];
        public List<string> Operations { get; } = [];

        public Task<PaymentEntity?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_payments.FirstOrDefault(payment => payment.Id == id));

        public Task<PaymentEntity> AddAsync(PaymentEntity entity, CancellationToken ct)
        {
            _payments.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(PaymentEntity entity)
        {
        }

        public void Remove(PaymentEntity entity)
            => _payments.Remove(entity);

        public IQueryable<PaymentEntity> Query()
            => _payments.AsQueryable();

        public IQueryable<PaymentEntity> QueryNoTracking()
            => _payments.AsQueryable();

        public Task<PaymentEntity?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
            => Task.FromResult(_payments.FirstOrDefault(payment => payment.IdempotencyKey == idempotencyKey));

        public Task<PaymentEntity?> FindByReferenceAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
        {
            Operations.Add($"find-reference:{referenceType}");
            return Task.FromResult(_payments.FirstOrDefault(payment =>
                payment.ReferenceType == referenceType && payment.ReferenceId == referenceId));
        }

        public Task AcquirePaymentReferenceLockAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
        {
            Operations.Add($"lock:{referenceType}");
            AcquiredReferenceLocks.Add((referenceType, referenceId));
            return Task.CompletedTask;
        }

        public Task<WalletTransaction> DebitWalletBookingPaymentAsync(
            Guid userId,
            Guid bookingId,
            Money amount,
            CancellationToken cancellationToken)
        {
            if (userId != _userId || WalletBalance < amount)
            {
                throw new PaymentInsufficientWalletException("Wallet balance is insufficient for the booking payment.");
            }

            var before = WalletBalance;
            WalletBalance -= amount;
            var transaction = WalletTransaction.CreateBookingPaymentDebit(userId, bookingId, amount, before, WalletBalance);
            WalletTransactions.Add(transaction);
            return Task.FromResult(transaction);
        }

        public Task<WalletTransaction> DebitWalletPaymentAsync(
            Guid userId, Guid referenceId, Money amount, WalletTransactionRef walletRef, CancellationToken ct)
        {
            if (userId != _userId || WalletBalance < amount)
            {
                throw new PaymentInsufficientWalletException("Wallet balance is insufficient for the payment.");
            }

            var before = WalletBalance;
            WalletBalance -= amount;
            var transaction = WalletTransaction.CreatePaymentDebit(userId, referenceId, amount, before, WalletBalance, walletRef);
            WalletTransactions.Add(transaction);
            return Task.FromResult(transaction);
        }

        public Task<IReadOnlyList<PaymentEntity>> ExpirePendingRedirectOlderThanAsync(
            DateTimeOffset expiresBefore,
            DateTimeOffset expiredAt,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PaymentEntity>>([]);

        public Task<bool> TryMarkRefundedByReferenceAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            DateTimeOffset refundedAt,
            CancellationToken cancellationToken)
            => Task.FromResult(false);
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
            => throw new NotSupportedException("Charge tests use platform wallet credit only.");

        public Task<PlatformWalletTransaction> CreditAsync(
            Money amount,
            PlatformWalletTransactionRef referenceType,
            Guid? referenceId,
            string? note,
            CancellationToken cancellationToken)
        {
            var before = Balance;
            Balance += amount;
            var transaction = PlatformWalletTransaction.Create(
                PlatformWalletTransactionType.CREDIT,
                amount,
                before,
                Balance,
                referenceType,
                referenceId,
                note);
            _transactions.Add(transaction);
            return Task.FromResult(transaction);
        }

        public Task<PlatformWalletTransaction> DebitAsync(
            Money amount,
            PlatformWalletTransactionRef referenceType,
            Guid? referenceId,
            string? note,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Charge tests do not debit platform wallet.");
    }

    private sealed class FakeVnPayClient : IVnPayClient
    {
        public FakeVnPayClient(string redirectUrl)
        {
            RedirectUrl = redirectUrl;
        }

        public string RedirectUrl { get; }

        public string CreateTopUpRedirectUrl(
            Guid userId,
            Money amount,
            string vnPayTxnRef,
            string clientIpAddress,
            DateTimeOffset createdAt)
        {
            vnPayTxnRef.Should().NotBeEmpty();
            clientIpAddress.Should().Be("203.0.113.10");
            createdAt.Should().Be(Now);
            return RedirectUrl;
        }

        public string CreateBookingPaymentRedirectUrl(
            Guid bookingId,
            Guid userId,
            Money amount,
            string vnPayTxnRef,
            string clientIpAddress,
            DateTimeOffset createdAt)
        {
            bookingId.Should().NotBeEmpty();
            vnPayTxnRef.Should().NotBeEmpty();
            clientIpAddress.Should().Be("203.0.113.10");
            createdAt.Should().Be(Now);
            return RedirectUrl;
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

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class NoOpRevenueLedgerWriter : IRevenueLedgerWriter
    {
        public Task RecordPaymentSucceededAsync(
            Guid sourceEventId,
            PaymentContextV1 context,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
