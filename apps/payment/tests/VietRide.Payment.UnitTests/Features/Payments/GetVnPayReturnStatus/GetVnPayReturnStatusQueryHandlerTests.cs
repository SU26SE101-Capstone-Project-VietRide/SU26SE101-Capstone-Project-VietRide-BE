using FluentAssertions;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Payments.GetVnPayReturnStatus;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.UnitTests.Features.Payments.GetVnPayReturnStatus;

public sealed class GetVnPayReturnStatusQueryHandlerTests
{
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
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html");
        var vnPay = new FakeVnPayClient(validSignature: true, expectedMerchant: true);
        var payments = new FakePaymentRepository(payment);
        var parameters = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = "TEST_MERCHANT",
            ["vnp_TxnRef"] = txnRef,
            ["vnp_SecureHash"] = "signed-hash",
        };
        var handler = new GetVnPayReturnStatusQueryHandler(vnPay, payments);

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
        var handler = new GetVnPayReturnStatusQueryHandler(vnPay, payments);

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
        var handler = new GetVnPayReturnStatusQueryHandler(vnPay, payments);

        var action = () => handler.Handle(
            new GetVnPayReturnStatusQuery(parameters),
            CancellationToken.None);

        await action.Should().ThrowAsync<CodedNotFoundException>()
            .Where(exception => exception.ErrorCode == "PAYMENT_NOT_FOUND");
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
            => Task.CompletedTask;

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
