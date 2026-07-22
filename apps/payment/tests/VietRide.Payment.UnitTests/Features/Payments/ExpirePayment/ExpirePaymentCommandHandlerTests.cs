using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Payments.ExpirePayment;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.UnitTests.Features.Payments.ExpirePayment;

public sealed class ExpirePaymentCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WhenPendingVnPayBookingPaymentIsOlderThan15Minutes_ExpiresItAndEnqueuesOutbox()
    {
        var bookingId = Guid.NewGuid();
        var stalePayment = CreatePendingVnPayBookingPayment(bookingId, Now.AddMinutes(-16));
        var repository = new FakePaymentRepository(stalePayment);
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(repository, outbox);

        var result = await handler.Handle(new ExpirePaymentCommand(Now), CancellationToken.None);

        result.ExpiredCount.Should().Be(1);
        stalePayment.Status.Should().Be(PaymentStatus.EXPIRED);
        stalePayment.ExpiredAt.Should().Be(Now);
        repository.LastExpiresBefore.Should().Be(Now.AddMinutes(-15));
        outbox.Events.Should().ContainSingle(evt => evt.EventType == "payment.payment.expired");
        using var payload = JsonDocument.Parse(outbox.Events.Single().PayloadJson);
        payload.RootElement.GetProperty("paymentId").GetGuid().Should().Be(stalePayment.Id);
        payload.RootElement.GetProperty("referenceType").GetString().Should().Be("BOOKING");
        payload.RootElement.GetProperty("referenceId").GetGuid().Should().Be(bookingId);
    }

    [Fact]
    public async Task Handle_WhenPaymentIsExactly15MinutesOld_LeavesItPendingAndDoesNotEnqueue()
    {
        var payment = CreatePendingVnPayBookingPayment(Guid.NewGuid(), Now.AddMinutes(-15));
        var repository = new FakePaymentRepository(payment);
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(repository, outbox);

        var result = await handler.Handle(new ExpirePaymentCommand(Now), CancellationToken.None);

        result.ExpiredCount.Should().Be(0);
        payment.Status.Should().Be(PaymentStatus.PENDING_REDIRECT);
        payment.ExpiredAt.Should().BeNull();
        outbox.Events.Should().BeEmpty();
    }

    private static ExpirePaymentCommandHandler CreateHandler(
        FakePaymentRepository repository,
        FakeIntegrationEventOutbox outbox)
        => new(
            repository,
            outbox,
            new FrozenClock(Now),
            NullLogger<ExpirePaymentCommandHandler>.Instance);

    private static PaymentEntity CreatePendingVnPayBookingPayment(Guid bookingId, DateTimeOffset createdAt)
    {
        var payment = PaymentEntity.CreatePendingRedirectVnPayBooking(
            bookingId,
            Guid.NewGuid(),
            Money.FromRaw(250_000),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("N"),
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html");
        payment.CreatedAt = createdAt;
        payment.UpdatedAt = createdAt;
        return payment;
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly List<PaymentEntity> _payments;

        public FakePaymentRepository(params PaymentEntity[] payments)
        {
            _payments = payments.ToList();
        }

        public DateTimeOffset? LastExpiresBefore { get; private set; }

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
            => throw new NotSupportedException("Payment expiration tests do not debit wallets.");

        public Task<WalletTransaction> DebitWalletPaymentAsync(
            Guid userId, Guid referenceId, Money amount, WalletTransactionRef walletRef, CancellationToken ct)
            => throw new NotSupportedException("Payment expiration tests do not debit wallets.");

        public Task<IReadOnlyList<PaymentEntity>> ExpirePendingRedirectOlderThanAsync(
            DateTimeOffset expiresBefore,
            DateTimeOffset expiredAt,
            CancellationToken cancellationToken)
        {
            LastExpiresBefore = expiresBefore;
            var expired = _payments
                .Where(payment =>
                    payment.Status == PaymentStatus.PENDING_REDIRECT
                    && payment.Method == PaymentMethod.VNPAY
                    && payment.ReferenceType == PaymentReferenceType.BOOKING
                    && payment.CreatedAt < expiresBefore)
                .ToList();

            foreach (var payment in expired)
            {
                payment.MarkExpired(expiredAt);
            }

            return Task.FromResult<IReadOnlyList<PaymentEntity>>(expired);
        }

        public Task<bool> TryMarkRefundedByReferenceAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            DateTimeOffset refundedAt,
            CancellationToken cancellationToken)
            => Task.FromResult(false);
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
}
