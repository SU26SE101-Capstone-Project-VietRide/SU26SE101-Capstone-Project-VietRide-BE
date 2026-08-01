using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Features.Payments.MarkPaymentRefunded;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.UnitTests.Features.Payments.MarkPaymentRefunded;

public sealed class MarkPaymentRefundedCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_WhenParcelRefundCredit_MarksMatchingPaymentRefunded()
    {
        var referenceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var payments = new FakePaymentRepository(
            transitionResult: true,
            PaymentReferenceType.PARCEL,
            referenceId,
            userId);
        var handler = CreateHandler(payments);

        await handler.HandleAsync(
            new WalletCreditedConsumerEvent(userId, 175_000, "PARCEL_REFUND", referenceId),
            CancellationToken.None);

        payments.RefundCalls.Should().ContainSingle(call =>
            call.ReferenceType == PaymentReferenceType.PARCEL
            && call.ReferenceId == referenceId
            && call.RefundedAt == Now);
    }

    [Fact]
    public async Task HandleAsync_WhenCorrelatedGenericBookingRefundIsExact_MarksOnlyFundingPayment()
    {
        var referenceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var payments = new FakePaymentRepository(
            transitionResult: true,
            PaymentReferenceType.BOOKING,
            referenceId,
            userId);
        var transaction = CreateGenericRefund(userId, referenceId, 175_000);
        var handler = CreateHandler(payments, new FakeWalletRepository(transaction));

        await handler.HandleAsync(
            new WalletCreditedConsumerEvent(
                userId,
                175_000,
                "BOOKING_REFUND",
                referenceId,
                payments.Payment.Id),
            CancellationToken.None);

        payments.RefundCalls.Should().ContainSingle(call =>
            call.ReferenceType == PaymentReferenceType.BOOKING
            && call.ReferenceId == referenceId
            && call.RefundedAt == Now);
    }

    [Fact]
    public async Task HandleAsync_WhenCorrelatedDirectBookingRefundIsPartial_MarksOnlyFundingPayment()
    {
        var referenceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var payments = new FakePaymentRepository(
            transitionResult: true,
            PaymentReferenceType.BOOKING,
            referenceId,
            userId);
        var handler = CreateHandler(
            payments,
            new FakeWalletRepository(CreateGenericRefund(userId, referenceId, 87_500)));

        await handler.HandleAsync(
            new WalletCreditedConsumerEvent(
                userId,
                87_500,
                "BOOKING_REFUND",
                referenceId,
                payments.Payment.Id),
            CancellationToken.None);

        payments.RefundCalls.Should().ContainSingle(call =>
            call.ReferenceType == PaymentReferenceType.BOOKING
            && call.ReferenceId == referenceId);
    }

    [Fact]
    public async Task HandleAsync_WhenLegacyGenericPayloadHasTransactionCorrelation_ResolvesUniqueFundingPayment()
    {
        var referenceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var payments = new FakePaymentRepository(
            transitionResult: true,
            PaymentReferenceType.BOOKING,
            referenceId,
            userId);
        var transaction = CreateGenericRefund(userId, referenceId, 175_000);
        var handler = CreateHandler(payments, new FakeWalletRepository(transaction));
        var integrationEvent = new WalletCreditedConsumerEvent(
            userId,
            175_000,
            "BOOKING_REFUND",
            referenceId);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        payments.RefundCalls.Should().ContainSingle(call =>
            call.ReferenceType == PaymentReferenceType.BOOKING
            && call.ReferenceId == referenceId);
    }

    [Fact]
    public async Task HandleAsync_WhenNotRefundCredit_DoesNothing()
    {
        var payments = new FakePaymentRepository(
            transitionResult: true,
            PaymentReferenceType.BOOKING,
            Guid.NewGuid(),
            Guid.NewGuid());
        var handler = CreateHandler(payments);

        await handler.HandleAsync(
            new WalletCreditedConsumerEvent(Guid.NewGuid(), 100_000, "TOP_UP", Guid.NewGuid()),
            CancellationToken.None);

        payments.RefundCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyRefunded_IsIdempotentNoOp()
    {
        var referenceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var payments = new FakePaymentRepository(
            transitionResult: false,
            PaymentReferenceType.BOOKING,
            referenceId,
            userId);
        var handler = CreateHandler(
            payments,
            new FakeWalletRepository(CreateGenericRefund(userId, referenceId, 175_000)));

        var act = async () => await handler.HandleAsync(
            new WalletCreditedConsumerEvent(
                userId,
                175_000,
                "BOOKING_REFUND",
                referenceId,
                payments.Payment.Id),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        payments.RefundCalls.Should().ContainSingle();
    }

    private static WalletTransaction CreateGenericRefund(Guid userId, Guid bookingId, long amount)
        => WalletTransaction.CreateBookingRefundCredit(
            userId,
            bookingId,
            Money.FromRaw(amount),
            Money.Zero,
            Money.FromRaw(amount));

    private static MarkPaymentRefundedCommandHandler CreateHandler(
        FakePaymentRepository payments,
        IWalletRepository? wallets = null)
        => new(
            payments,
            wallets ?? new FakeWalletRepository(),
            new FixedClock(Now),
            new FakeIntegrationEventOutbox(),
            NullLogger<MarkPaymentRefundedCommandHandler>.Instance);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeWalletRepository(params WalletTransaction[] transactions) : IWalletRepository
    {
        public Task<WalletTransaction?> FindTransactionByIdAsync(
            Guid transactionId,
            CancellationToken cancellationToken)
            => Task.FromResult<WalletTransaction?>(
                transactions.SingleOrDefault(transaction => transaction.Id == transactionId));

        public Task<IReadOnlyList<WalletTransaction>> ListRefundTransactionsByReferenceAsync(
            WalletTransactionRef referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<WalletTransaction>>(
                transactions.Where(transaction =>
                    transaction.ReferenceType == referenceType
                    && transaction.ReferenceId == referenceId).ToArray());

        public Task<bool> EnsureBootstrapWalletAsync(Guid userId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Wallet?> GetByIdAsync(Guid id, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<Wallet> AddAsync(Wallet entity, CancellationToken ct)
            => throw new NotSupportedException();

        public void Update(Wallet entity)
            => throw new NotSupportedException();

        public void Remove(Wallet entity)
            => throw new NotSupportedException();

        public IQueryable<Wallet> Query()
            => throw new NotSupportedException();

        public IQueryable<Wallet> QueryNoTracking()
            => throw new NotSupportedException();
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly bool _transitionResult;

        public FakePaymentRepository(
            bool transitionResult,
            PaymentReferenceType referenceType,
            Guid referenceId,
            Guid userId)
        {
            _transitionResult = transitionResult;
            Payment = CreatePayment(referenceType, referenceId, userId);
        }

        public PaymentEntity Payment { get; }

        public List<(PaymentReferenceType ReferenceType, Guid ReferenceId, DateTimeOffset RefundedAt)>
            RefundCalls
        { get; } = [];

        public Task<PaymentEntity?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<PaymentEntity?>(id == Payment.Id ? Payment : null);

        public Task<PaymentEntity?> FindByReferenceAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
            => Task.FromResult<PaymentEntity?>(
                Payment.ReferenceType == referenceType && Payment.ReferenceId == referenceId
                    ? Payment
                    : null);

        public Task<IReadOnlyList<PaymentEntity>> ListBookingPaymentAttemptsByAllocationAsync(
            Guid bookingId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PaymentEntity>>([Payment]);

        public Task<IReadOnlyList<PaymentEntity>> ListSucceededBookingFundingPaymentsByAllocationAsync(
            Guid bookingId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PaymentEntity>>(
                Payment.ReferenceType == PaymentReferenceType.BOOKING
                && Payment.ReferenceId == bookingId
                    ? [Payment]
                    : []);

        public Task AcquireRefundReconciliationLockAsync(
            Guid paymentId,
            CancellationToken cancellationToken)
        {
            paymentId.Should().Be(Payment.Id);
            return Task.CompletedTask;
        }

        public Task<bool> TryMarkRefundedByIdAsync(
            Guid paymentId,
            DateTimeOffset refundedAt,
            CancellationToken cancellationToken)
        {
            paymentId.Should().Be(Payment.Id);
            RefundCalls.Add((Payment.ReferenceType, Payment.ReferenceId, refundedAt));
            return Task.FromResult(_transitionResult);
        }

        public Task<PaymentEntity> AddAsync(PaymentEntity entity, CancellationToken ct)
            => throw new NotSupportedException();

        public void Update(PaymentEntity entity)
            => throw new NotSupportedException();

        public void Remove(PaymentEntity entity)
            => throw new NotSupportedException();

        public IQueryable<PaymentEntity> Query()
            => throw new NotSupportedException();

        public IQueryable<PaymentEntity> QueryNoTracking()
            => throw new NotSupportedException();

        public Task<PaymentEntity?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AcquirePaymentReferenceLockAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

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
            => throw new NotSupportedException();

        public Task<bool> TryMarkRefundedByReferenceAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            DateTimeOffset refundedAt,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        private static PaymentEntity CreatePayment(
            PaymentReferenceType referenceType,
            Guid referenceId,
            Guid userId)
        {
            var payment = PaymentEntity.CreateSucceededWalletCharge(
                referenceType,
                referenceId,
                userId,
                Money.FromRaw(175_000),
                Now.AddDays(-1));
            payment.AttachContext(PaymentContextCodec.ValidateAndSerialize(
                new PaymentContextV1(1,
                [
                    new PaymentAllocationV1(
                        referenceId,
                        referenceType.ToString(),
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        175_000,
                        0,
                        0),
                ]),
                referenceType.ToString(),
                referenceId,
                175_000));
            return payment;
        }
    }

    private sealed class FakeIntegrationEventOutbox : IIntegrationEventOutbox
    {
        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
