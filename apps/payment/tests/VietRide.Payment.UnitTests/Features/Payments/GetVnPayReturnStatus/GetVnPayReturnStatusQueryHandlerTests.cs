using FluentAssertions;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Features.Payments.GetVnPayReturnStatus;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.UnitTests.Features.Payments.GetVnPayReturnStatus;

public sealed class GetVnPayReturnStatusQueryHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-03T03:00:00Z");

    [Fact]
    public async Task SignedSubscriptionCancel_MarksPendingPaymentFailedAndEnqueuesTerminalEvent()
    {
        const string txnRef = "VR-SUBSCRIPTION-CANCEL-001";
        var payment = CreateSubscriptionPayment(txnRef);
        var vnPay = new FakeVnPayClient(validSignature: true, expectedMerchant: true);
        var payments = new FakePaymentRepository(payment);
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(vnPay, payments, outbox);

        var result = await handler.Handle(
            new GetVnPayReturnStatusQuery(SignedParameters(txnRef, "24", "02")),
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.FAILED.ToString());
        payment.Status.Should().Be(PaymentStatus.FAILED);
        payment.VnPayResponseCode.Should().Be("24");
        payment.FailedAt.Should().Be(Now);
        payments.UpdateCallCount.Should().Be(1);
        payments.LockCallCount.Should().Be(1);
        outbox.Events.Should().ContainSingle();
        outbox.Events[0].EventType.Should().Be(new SubscriptionPaymentFailedIntegrationEvent(
            payment.Id,
            payment.ReferenceId,
            payment.OperatorId!.Value,
            Guid.NewGuid(),
            "24").EventType);
        outbox.Events[0].Payload.Should().Contain(payment.Id.ToString());
        outbox.Events[0].Payload.Should().Contain(payment.ReferenceId.ToString());
        outbox.Events[0].Payload.Should().Contain("\"responseCode\":\"24\"");
    }

    [Fact]
    public async Task RepeatedSignedSubscriptionCancel_IsIdempotent()
    {
        const string txnRef = "VR-SUBSCRIPTION-CANCEL-REPLAY";
        var payment = CreateSubscriptionPayment(txnRef);
        var payments = new FakePaymentRepository(payment);
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(
            new FakeVnPayClient(validSignature: true, expectedMerchant: true),
            payments,
            outbox);
        var query = new GetVnPayReturnStatusQuery(SignedParameters(txnRef, "24", "02"));

        await handler.Handle(query, CancellationToken.None);
        var replay = await handler.Handle(query, CancellationToken.None);

        replay.Status.Should().Be(PaymentStatus.FAILED.ToString());
        payments.UpdateCallCount.Should().Be(1);
        outbox.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task SignedSubscriptionSuccess_RemainsPendingUntilIpn()
    {
        const string txnRef = "VR-SUBSCRIPTION-SUCCESS-RETURN";
        var payment = CreateSubscriptionPayment(txnRef);
        var payments = new FakePaymentRepository(payment);
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(
            new FakeVnPayClient(validSignature: true, expectedMerchant: true),
            payments,
            outbox);

        var result = await handler.Handle(
            new GetVnPayReturnStatusQuery(SignedParameters(txnRef, "00", "00")),
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.PENDING_REDIRECT.ToString());
        payment.Status.Should().Be(PaymentStatus.PENDING_REDIRECT);
        payments.UpdateCallCount.Should().Be(0);
        payments.LockCallCount.Should().Be(0);
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task SignedSubscriptionCancelWithMismatchedAmount_IsRejectedWithoutMutation()
    {
        const string txnRef = "VR-SUBSCRIPTION-CANCEL-BAD-AMOUNT";
        var payment = CreateSubscriptionPayment(txnRef);
        var payments = new FakePaymentRepository(payment);
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(
            new FakeVnPayClient(validSignature: true, expectedMerchant: true),
            payments,
            outbox);
        var parameters = SignedParameters(txnRef, "24", "02");
        parameters["vnp_Amount"] = "14900000";

        var action = () => handler.Handle(
            new GetVnPayReturnStatusQuery(parameters),
            CancellationToken.None);

        await action.Should().ThrowAsync<CodedValidationException>()
            .Where(exception => exception.ErrorCode == "PAYMENT_AMOUNT_INVALID");
        payment.Status.Should().Be(PaymentStatus.PENDING_REDIRECT);
        payments.UpdateCallCount.Should().Be(0);
        payments.LockCallCount.Should().Be(0);
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task SignedSubscriptionCancelAfterIpnSuccess_DoesNotDowngradePayment()
    {
        const string txnRef = "VR-SUBSCRIPTION-CANCEL-AFTER-SUCCESS";
        var payment = CreateSubscriptionPayment(txnRef);
        payment.MarkSucceeded("00", Now.AddSeconds(-1));
        var payments = new FakePaymentRepository(payment);
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(
            new FakeVnPayClient(validSignature: true, expectedMerchant: true),
            payments,
            outbox);

        var result = await handler.Handle(
            new GetVnPayReturnStatusQuery(SignedParameters(txnRef, "24", "02")),
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.SUCCEEDED.ToString());
        payment.Status.Should().Be(PaymentStatus.SUCCEEDED);
        payment.VnPayResponseCode.Should().Be("00");
        payments.UpdateCallCount.Should().Be(0);
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task SignedSubscriptionCancelAfterExpiry_DoesNotOverwritePayment()
    {
        const string txnRef = "VR-SUBSCRIPTION-CANCEL-AFTER-EXPIRY";
        var payment = CreateSubscriptionPayment(txnRef);
        payment.MarkExpired(Now.AddSeconds(-1));
        var payments = new FakePaymentRepository(payment);
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(
            new FakeVnPayClient(validSignature: true, expectedMerchant: true),
            payments,
            outbox);

        var result = await handler.Handle(
            new GetVnPayReturnStatusQuery(SignedParameters(txnRef, "24", "02")),
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.EXPIRED.ToString());
        payment.Status.Should().Be(PaymentStatus.EXPIRED);
        payment.VnPayResponseCode.Should().BeNull();
        payments.UpdateCallCount.Should().Be(0);
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidSignedReturn_ReturnsPersistedStatusWithoutMutation()
    {
        const string txnRef = "VR-RETURN-001";
        var payment = PaymentEntity.CreatePendingRedirectVnPayBooking(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.FromRaw(150_000),
            txnRef,
            "return-status-idempotency",
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
            VnPayReturnMode.OPERATOR_WEB);
        var vnPay = new FakeVnPayClient(validSignature: true, expectedMerchant: true);
        var payments = new FakePaymentRepository(payment);
        var parameters = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = "TEST_MERCHANT",
            ["vnp_TxnRef"] = txnRef,
            ["vnp_Amount"] = "15000000",
            ["vnp_SecureHash"] = "signed-hash",
        };
        var handler = CreateHandler(vnPay, payments);

        var result = await handler.Handle(
            new GetVnPayReturnStatusQuery(parameters),
            CancellationToken.None);

        result.VnPayTxnRef.Should().Be(txnRef);
        result.PaymentId.Should().Be(payment.Id);
        result.ReferenceType.Should().Be(PaymentReferenceType.BOOKING.ToString());
        result.ReferenceId.Should().Be(payment.ReferenceId);
        result.Status.Should().Be(PaymentStatus.PENDING_REDIRECT.ToString());
        payment.Status.Should().Be(PaymentStatus.PENDING_REDIRECT);
        payments.UpdateCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task InvalidSignatureOrMerchant_IsRejected(
        bool validSignature,
        bool expectedMerchant)
    {
        var vnPay = new FakeVnPayClient(validSignature, expectedMerchant);
        var payments = new FakePaymentRepository();
        var parameters = new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = "VR-RETURN-INVALID",
        };
        var handler = CreateHandler(vnPay, payments);

        var action = () => handler.Handle(
            new GetVnPayReturnStatusQuery(parameters),
            CancellationToken.None);

        await action.Should().ThrowAsync<UnauthorizedException>()
            .Where(exception => exception.ErrorCode == "PAYMENT_SIGNATURE_INVALID");
        payments.QueryNoTrackingCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ValidSignedReturnForUnknownTransaction_IsNotFound()
    {
        var vnPay = new FakeVnPayClient(validSignature: true, expectedMerchant: true);
        var payments = new FakePaymentRepository();
        var parameters = new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = "VR-RETURN-MISSING",
        };
        var handler = CreateHandler(vnPay, payments);

        var action = () => handler.Handle(
            new GetVnPayReturnStatusQuery(parameters),
            CancellationToken.None);

        await action.Should().ThrowAsync<CodedNotFoundException>()
            .Where(exception => exception.ErrorCode == "PAYMENT_NOT_FOUND");
    }

    private static GetVnPayReturnStatusQueryHandler CreateHandler(
        IVnPayClient vnPay,
        IPaymentRepository payments,
        IIntegrationEventOutbox? outbox = null)
        => new(
            vnPay,
            payments,
            outbox ?? new FakeIntegrationEventOutbox(),
            new FrozenClock(Now));

    private static PaymentEntity CreateSubscriptionPayment(string txnRef)
    {
        var payment = PaymentEntity.CreatePendingRedirectVnPaySubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.FromRaw(150_000),
            txnRef,
            $"subscription-{txnRef}",
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
            Now.AddMinutes(15));
        var context = new SubscriptionPaymentContextV1(
            1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Gói Pro",
            "MONTHLY",
            Now,
            Now.AddMonths(1),
            new SubscriptionBuyerSnapshotV1(
                "Nhà xe kiểm thử",
                "0312345678",
                "0312345678",
                "operator@example.test",
                "0900000000",
                null,
                null,
                null));
        payment.AttachContext(SubscriptionPaymentContextCodec.ValidateAndSerialize(
            context,
            context.OperatorSubscriptionId));
        return payment;
    }

    private static Dictionary<string, string> SignedParameters(
        string txnRef,
        string responseCode,
        string transactionStatus)
        => new()
        {
            ["vnp_TmnCode"] = "TEST_MERCHANT",
            ["vnp_TxnRef"] = txnRef,
            ["vnp_Amount"] = "15000000",
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_TransactionStatus"] = transactionStatus,
            ["vnp_SecureHash"] = "signed-hash",
        };

    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeIntegrationEventOutbox : IIntegrationEventOutbox
    {
        public List<(string EventType, string Payload)> Events { get; } = [];

        public Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken ct = default)
        {
            Events.Add((eventType, payloadJson));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeVnPayClient(bool validSignature, bool expectedMerchant) : IVnPayClient
    {
        public string CreateTopUpRedirectUrl(
            Guid userId,
            Money amount,
            string vnPayTxnRef,
            string clientIpAddress,
            DateTimeOffset createdAt)
            => throw new NotSupportedException();

        public bool VerifySignature(IReadOnlyDictionary<string, string> parameters)
            => validSignature;

        public bool IsExpectedMerchant(IReadOnlyDictionary<string, string> parameters)
            => expectedMerchant;
    }

    private sealed class FakePaymentRepository(params PaymentEntity[] payments) : IPaymentRepository
    {
        private readonly List<PaymentEntity> _payments = payments.ToList();

        public int UpdateCallCount { get; private set; }
        public int QueryNoTrackingCallCount { get; private set; }
        public int LockCallCount { get; private set; }

        public Task<PaymentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(_payments.FirstOrDefault(payment => payment.Id == id));

        public Task<PaymentEntity> AddAsync(PaymentEntity entity, CancellationToken cancellationToken)
        {
            _payments.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(PaymentEntity entity)
            => UpdateCallCount++;

        public void Remove(PaymentEntity entity)
            => _payments.Remove(entity);

        public IQueryable<PaymentEntity> Query()
            => _payments.AsQueryable();

        public IQueryable<PaymentEntity> QueryNoTracking()
        {
            QueryNoTrackingCallCount++;
            return _payments.AsQueryable();
        }

        public Task<PaymentEntity?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken)
            => Task.FromResult(_payments.FirstOrDefault(payment =>
                payment.IdempotencyKey == idempotencyKey));

        public Task<PaymentEntity?> FindByReferenceAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
            => Task.FromResult(_payments.FirstOrDefault(payment =>
                payment.ReferenceType == referenceType
                && payment.ReferenceId == referenceId));

        public Task AcquirePaymentReferenceLockAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
        {
            LockCallCount++;
            return Task.CompletedTask;
        }

        public Task<WalletTransaction> DebitWalletBookingPaymentAsync(
            Guid userId,
            Guid bookingId,
            Money amount,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<WalletTransaction> DebitWalletPaymentAsync(
            Guid userId,
            Guid referenceId,
            Money amount,
            WalletTransactionRef walletRef,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PaymentEntity>> ExpirePendingRedirectDueAsync(
            DateTimeOffset legacyCreatedAtOrBefore,
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
}
