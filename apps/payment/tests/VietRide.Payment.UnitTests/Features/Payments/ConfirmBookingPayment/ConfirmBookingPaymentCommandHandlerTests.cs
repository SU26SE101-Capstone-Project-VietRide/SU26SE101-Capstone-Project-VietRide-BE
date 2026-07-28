using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Features.Payments.ConfirmBookingPayment;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.UnitTests.Features.Payments.ConfirmBookingPayment;

public sealed class ConfirmBookingPaymentCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WhenIpnIsValid_CreditsPlatformWalletAndEnqueuesSucceededEvent()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var payment = CreatePendingPayment(userId, bookingId, "txn-1", 250_000);
        var payments = new FakePaymentRepository(payment);
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(1_000_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(new FakeVnPayClient(isSignatureValid: true), payments, platformWallets, outbox);

        var result = await handler.Handle(CreateCommand("txn-1", "00", "25000000"), CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.RspCode.Should().Be("00");
        payment.Status.Should().Be(PaymentStatus.SUCCEEDED);
        payment.SucceededAt.Should().Be(Now);
        platformWallets.Balance.Should().Be(Money.FromRaw(1_250_000));
        platformWallets.Transactions.Should().ContainSingle(tx =>
            tx.ReferenceType == PlatformWalletTransactionRef.BOOKING_PAYMENT_HOLD
            && tx.ReferenceId == bookingId);
        outbox.Events.Should().ContainSingle(evt => evt.EventType == "payment.payment.succeeded");
        using var payload = JsonDocument.Parse(outbox.Events.Single().Payload);
        payload.RootElement.GetProperty("paymentId").GetGuid().Should().Be(payment.Id);
        payload.RootElement.GetProperty("referenceType").GetString().Should().Be("BOOKING");
        payload.RootElement.GetProperty("referenceId").GetGuid().Should().Be(bookingId);
        payload.RootElement.GetProperty("amount").GetInt64().Should().Be(250_000);
    }

    [Fact]
    public async Task Handle_WhenSameIpnReplays_DoesNotDoubleCreditOrEnqueue()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var payment = CreatePendingPayment(userId, bookingId, "txn-1", 250_000);
        var payments = new FakePaymentRepository(payment);
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(1_000_000));
        var outbox = new FakeIntegrationEventOutbox();
        var vnPay = new FakeVnPayClient(isSignatureValid: true);
        var handler = CreateHandler(vnPay, payments, platformWallets, outbox);

        await handler.Handle(CreateCommand("txn-1", "00", "25000000"), CancellationToken.None);
        var replay = await handler.Handle(CreateCommand("txn-1", "00", "25000000"), CancellationToken.None);

        replay.StatusCode.Should().Be(200);
        replay.RspCode.Should().Be("02");
        platformWallets.Transactions.Should().ContainSingle();
        platformWallets.Balance.Should().Be(Money.FromRaw(1_250_000));
        outbox.Events.Should().ContainSingle(evt => evt.EventType == "payment.payment.succeeded");
        vnPay.ReservedTxnRefs.Should().HaveCount(2);
        vnPay.ReleasedTxnRefs.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenSignatureIsInvalid_Returns401WithoutMutatingState()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var payment = CreatePendingPayment(userId, bookingId, "txn-1", 250_000);
        var payments = new FakePaymentRepository(payment);
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(1_000_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(new FakeVnPayClient(isSignatureValid: false), payments, platformWallets, outbox);

        var result = await handler.Handle(CreateCommand("txn-1", "00", "25000000"), CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.RspCode.Should().Be("97");
        result.Message.Should().Be("PAYMENT_SIGNATURE_INVALID");
        payment.Status.Should().Be(PaymentStatus.PENDING_REDIRECT);
        platformWallets.Transactions.Should().BeEmpty();
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_LateParcelPayment_UsesSignedPayDateAndEnqueuesRefund()
    {
        var userId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        var dueAt = Now.AddMinutes(-1);
        var payment = CreatePendingParcelPayment(
            userId,
            parcelId,
            "parcel-txn",
            80_000,
            dueAt);
        var payments = new FakePaymentRepository(payment);
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(1_000_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(
            new FakeVnPayClient(isSignatureValid: true),
            payments,
            platformWallets,
            outbox);

        await handler.Handle(
            CreateCommand(
                "parcel-txn",
                "00",
                "8000000",
                payDate: "20260624170100"),
            CancellationToken.None);

        payment.SucceededAt.Should().Be(new DateTimeOffset(2026, 6, 24, 17, 1, 0, TimeSpan.FromHours(7)));
        outbox.Events.Should().ContainSingle(evt => evt.EventType == "payment.payment.succeeded");
        outbox.Events.Should().ContainSingle(evt => evt.EventType == "parcel.refund.initiated");
        using var succeeded = JsonDocument.Parse(
            outbox.Events.Single(evt => evt.EventType == "payment.payment.succeeded").Payload);
        succeeded.RootElement.GetProperty("paidAt").GetDateTimeOffset().Should().Be(payment.SucceededAt);
        succeeded.RootElement.GetProperty("dueAt").GetDateTimeOffset().Should().Be(dueAt);
        using var refund = JsonDocument.Parse(
            outbox.Events.Single(evt => evt.EventType == "parcel.refund.initiated").Payload);
        refund.RootElement.GetProperty("amount").GetInt64().Should().Be(80_000);
        refund.RootElement.GetProperty("idempotencyKey").GetString()
            .Should().Be($"{payment.Id:D}:LATE_PAYMENT");
    }

    [Fact]
    public async Task Handle_WhenVnPayResponseCodeFails_MarksFailedAndEnqueuesFailedEvent()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var payment = CreatePendingPayment(userId, bookingId, "txn-1", 250_000);
        var payments = new FakePaymentRepository(payment);
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(1_000_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(new FakeVnPayClient(isSignatureValid: true), payments, platformWallets, outbox);

        var result = await handler.Handle(CreateCommand("txn-1", "24", "25000000"), CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.RspCode.Should().Be("00");
        payment.Status.Should().Be(PaymentStatus.FAILED);
        payment.FailedAt.Should().Be(Now);
        payment.VnPayResponseCode.Should().Be("24");
        platformWallets.Transactions.Should().BeEmpty();
        outbox.Events.Should().ContainSingle(evt => evt.EventType == "payment.payment.failed");
        outbox.Events.Single().Payload.Should().Contain("\"reason\":\"24\"");
    }

    private static ConfirmBookingPaymentCommandHandler CreateHandler(
        IVnPayClient vnPayClient,
        IPaymentRepository payments,
        IPlatformWalletRepository platformWallets,
        IIntegrationEventOutbox outbox)
        => new(
            vnPayClient,
            payments,
            platformWallets,
            outbox,
            new NoOpRevenueLedgerWriter(),
            new FrozenClock(Now),
            NullLogger<ConfirmBookingPaymentCommandHandler>.Instance);

    private static ConfirmBookingPaymentCommand CreateCommand(
        string txnRef,
        string responseCode,
        string signedAmount,
        string transactionStatus = "00",
        string? payDate = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = txnRef,
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_Amount"] = signedAmount,
            ["vnp_TransactionStatus"] = transactionStatus,
            ["vnp_SecureHash"] = "valid",
        };
        if (payDate is not null)
            parameters["vnp_PayDate"] = payDate;
        return new ConfirmBookingPaymentCommand(parameters);
    }

    private static PaymentEntity CreatePendingPayment(Guid userId, Guid bookingId, string txnRef, long amount)
    {
        var payment = PaymentEntity.CreatePendingRedirectVnPayBooking(
            bookingId,
            userId,
            Money.FromRaw(amount),
            txnRef,
            $"idem-{txnRef}",
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html");
        payment.AttachContext(PaymentContextCodec.ValidateAndSerialize(
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
            "BOOKING",
            bookingId,
            amount));
        return payment;
    }

    private static PaymentEntity CreatePendingParcelPayment(
        Guid userId,
        Guid parcelId,
        string txnRef,
        long amount,
        DateTimeOffset dueAt)
    {
        var payment = PaymentEntity.CreatePendingRedirectVnPay(
            PaymentReferenceType.PARCEL_ADDITIONAL,
            parcelId,
            userId,
            Money.FromRaw(amount),
            txnRef,
            $"idem-{txnRef}",
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
            dueAt);
        payment.AttachContext(PaymentContextCodec.ValidateAndSerialize(
            new PaymentContextV1(1,
            [
                new PaymentAllocationV1(
                    parcelId,
                    "PARCEL_ADDITIONAL",
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    amount,
                    0,
                    0),
            ]),
            "PARCEL_ADDITIONAL",
            parcelId,
            amount));
        return payment;
    }

    [Fact]
    public async Task Handle_WhenLegacyPendingPaymentHasNoContext_SettlesAndMarksReconciliationWithoutSuccessEvent()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var payment = PaymentEntity.CreatePendingRedirectVnPayBooking(
            bookingId,
            userId,
            Money.FromRaw(250_000),
            "legacy-txn",
            "legacy-idempotency-key",
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html");
        var payments = new FakePaymentRepository(payment);
        var platformWallets = new FakePlatformWalletRepository(Money.FromRaw(1_000_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(
            new FakeVnPayClient(isSignatureValid: true),
            payments,
            platformWallets,
            outbox);

        var result = await handler.Handle(
            CreateCommand("legacy-txn", "00", "25000000"),
            CancellationToken.None);

        result.RspCode.Should().Be("00");
        payment.Status.Should().Be(PaymentStatus.SUCCEEDED);
        payment.ContextReconciliationRequired.Should().BeTrue();
        platformWallets.Transactions.Should().ContainSingle();
        outbox.Events.Should().BeEmpty();
    }

    private sealed class FakeVnPayClient : IVnPayClient
    {
        private readonly bool _isSignatureValid;
        private readonly HashSet<string> _reserved = new(StringComparer.Ordinal);

        public FakeVnPayClient(bool isSignatureValid)
        {
            _isSignatureValid = isSignatureValid;
        }

        public List<string> ReservedTxnRefs { get; } = [];

        public List<string> ReleasedTxnRefs { get; } = [];

        public string CreateTopUpRedirectUrl(
            Guid userId,
            Money amount,
            string vnPayTxnRef,
            string clientIpAddress,
            DateTimeOffset createdAt)
            => throw new NotSupportedException("Confirm booking payment tests do not create redirects.");

        public bool VerifySignature(IReadOnlyDictionary<string, string> parameters)
            => _isSignatureValid;

        public Task<bool> TryReserveIpnAsync(string vnPayTxnRef, CancellationToken cancellationToken)
        {
            ReservedTxnRefs.Add(vnPayTxnRef);
            return Task.FromResult(_reserved.Add(vnPayTxnRef));
        }

        public Task ReleaseIpnReservationAsync(string vnPayTxnRef, CancellationToken cancellationToken)
        {
            ReleasedTxnRefs.Add(vnPayTxnRef);
            _reserved.Remove(vnPayTxnRef);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly List<PaymentEntity> _payments;

        public FakePaymentRepository(params PaymentEntity[] payments)
        {
            _payments = payments.ToList();
        }

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
            => Task.FromResult(_payments.FirstOrDefault(payment =>
                payment.ReferenceType == referenceType && payment.ReferenceId == referenceId));

        public Task AcquirePaymentReferenceLockAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WalletTransaction> DebitWalletBookingPaymentAsync(
            Guid userId,
            Guid bookingId,
            Money amount,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Confirm booking payment tests do not debit user wallets.");

        public Task<WalletTransaction> DebitWalletPaymentAsync(
            Guid userId, Guid referenceId, Money amount, WalletTransactionRef walletRef, CancellationToken ct)
            => throw new NotSupportedException("Confirm booking payment tests do not debit user wallets.");

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
            => throw new NotSupportedException("Confirm booking payment tests use credit only.");

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
            => throw new NotSupportedException("Confirm booking payment tests do not debit platform wallet.");
    }

    private sealed class FakeIntegrationEventOutbox : IIntegrationEventOutbox
    {
        public List<(string EventType, string Payload)> Events { get; } = [];

        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
        {
            Events.Add((eventType, payloadJson));
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpRevenueLedgerWriter : IRevenueLedgerWriter
    {
        public Task RecordPaymentSucceededAsync(
            Guid sourceEventId,
            PaymentContextV1 context,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
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
