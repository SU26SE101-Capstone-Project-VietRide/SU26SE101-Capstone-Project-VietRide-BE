using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Features.Payments.MarkPaymentRefunded;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.UnitTests.Features.Payments.MarkPaymentRefunded;

public sealed class MarkPaymentRefundedCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("BOOKING_REFUND", PaymentReferenceType.BOOKING)]
    [InlineData("PARCEL_REFUND", PaymentReferenceType.PARCEL)]
    public async Task HandleAsync_WhenRefundCredit_MarksMatchingPaymentRefunded(
        string referenceType,
        PaymentReferenceType expectedMapped)
    {
        var referenceId = Guid.NewGuid();
        var payments = new FakePaymentRepository(transitionResult: true);
        var handler = new MarkPaymentRefundedCommandHandler(payments, new FixedClock(Now), NullLogger<MarkPaymentRefundedCommandHandler>.Instance);

        await handler.HandleAsync(
            new WalletCreditedConsumerEvent(Guid.NewGuid(), 175_000, referenceType, referenceId),
            CancellationToken.None);

        payments.RefundCalls.Should().ContainSingle(call =>
            call.ReferenceType == expectedMapped
            && call.ReferenceId == referenceId
            && call.RefundedAt == Now);
    }

    [Fact]
    public async Task HandleAsync_WhenNotRefundCredit_DoesNothing()
    {
        var payments = new FakePaymentRepository(transitionResult: true);
        var handler = new MarkPaymentRefundedCommandHandler(payments, new FixedClock(Now), NullLogger<MarkPaymentRefundedCommandHandler>.Instance);

        await handler.HandleAsync(
            new WalletCreditedConsumerEvent(Guid.NewGuid(), 100_000, "TOP_UP", Guid.NewGuid()),
            CancellationToken.None);

        payments.RefundCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyRefunded_IsIdempotentNoOp()
    {
        var payments = new FakePaymentRepository(transitionResult: false);
        var handler = new MarkPaymentRefundedCommandHandler(payments, new FixedClock(Now), NullLogger<MarkPaymentRefundedCommandHandler>.Instance);

        // transitionResult: false simulates a re-delivery where the row is already REFUNDED — must not throw.
        var act = async () => await handler.HandleAsync(
            new WalletCreditedConsumerEvent(Guid.NewGuid(), 175_000, "BOOKING_REFUND", Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        payments.RefundCalls.Should().ContainSingle();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakePaymentRepository(bool transitionResult) : IPaymentRepository
    {
        public List<(PaymentReferenceType ReferenceType, Guid ReferenceId, DateTimeOffset RefundedAt)> RefundCalls { get; } = [];

        public Task<bool> TryMarkRefundedByReferenceAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            DateTimeOffset refundedAt,
            CancellationToken cancellationToken)
        {
            RefundCalls.Add((referenceType, referenceId, refundedAt));
            return Task.FromResult(transitionResult);
        }

        public Task<PaymentEntity?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<PaymentEntity> AddAsync(PaymentEntity entity, CancellationToken ct) => throw new NotSupportedException();
        public void Update(PaymentEntity entity) => throw new NotSupportedException();
        public void Remove(PaymentEntity entity) => throw new NotSupportedException();
        public IQueryable<PaymentEntity> Query() => throw new NotSupportedException();
        public IQueryable<PaymentEntity> QueryNoTracking() => throw new NotSupportedException();
        public Task<PaymentEntity?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymentEntity?> FindByReferenceAsync(PaymentReferenceType referenceType, Guid referenceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AcquirePaymentReferenceLockAsync(PaymentReferenceType referenceType, Guid referenceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WalletTransaction> DebitWalletBookingPaymentAsync(Guid userId, Guid bookingId, Money amount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PaymentEntity>> ExpirePendingRedirectOlderThanAsync(DateTimeOffset expiresBefore, DateTimeOffset expiredAt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
