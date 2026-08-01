using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using VietRide.Payment.Application;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Features.Payments.MarkPaymentRefunded;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Application.Services;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure;
using VietRide.Payment.Infrastructure.DependencyInjection;
using VietRide.Payment.Infrastructure.Jobs;
using VietRide.Payment.Infrastructure.Messaging;
using VietRide.Payment.Infrastructure.Refunds;
using VietRide.Shared.Application.DependencyInjection;
using VietRide.Shared.Application.Inbox;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Messaging.RabbitMq;
using VietRide.Shared.Persistence.DependencyInjection;
using VietRide.Shared.Persistence.Outbox;
using ProducerRefundRequestedEvent =
    VietRide.Booking.Application.Events.BookingPaymentRefundRequestedIntegrationEvent;

namespace VietRide.Payment.IntegrationTests.Messaging;

public sealed class BookingPaymentRefundRequestedIntegrationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-31T12:00:00Z");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ProducerPayload_DeliversThroughDurableInboxAndRefundsOnlyExactPaymentAttempt()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var bookingId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var first = CreateSucceededPayment(
                PaymentReferenceType.BOOKING,
                bookingId,
                userId,
                [Allocation(bookingId, 350_000)]);
            var second = CreateSucceededWalletPayment(
                PaymentReferenceType.BOOKING,
                bookingId,
                userId,
                [Allocation(bookingId, 350_000)]);
            await SeedAsync(connectionString, userId, first, second);

            var producer = new ProducerRefundRequestedEvent(
                first.Id,
                "BOOKING",
                bookingId,
                bookingId,
                userId,
                350_000,
                "PAYMENT_CAPTURE_AFTER_BOOKING_EXPIRY");
            var payload = JsonSerializer.Serialize(producer, JsonOptions);
            using (var document = JsonDocument.Parse(payload))
            {
                document.RootElement.TryGetProperty("eventType", out _).Should().BeFalse();
                document.RootElement.EnumerateObject().Select(property => property.Name)
                    .Should().BeEquivalentTo(
                        "eventId",
                        "occurredAt",
                        "paymentId",
                        "paymentReferenceType",
                        "paymentReferenceId",
                        "bookingId",
                        "userId",
                        "amount",
                        "reason");
            }

            var consumer = JsonSerializer.Deserialize<
                VietRide.Payment.Infrastructure.Messaging.BookingPaymentRefundRequestedIntegrationEvent>(
                payload,
                JsonOptions);
            consumer.Should().NotBeNull();
            consumer!.PaymentId.Should().Be(first.Id);
            consumer.EventId.Should().Be(producer.EventId);

            await using var provider = CreateProvider(connectionString);
            var processed = await DeliverAsync(provider, consumer, payload);
            var duplicate = await DeliverAsync(provider, consumer, payload);
            var walletCreditProcessed = await DeliverWalletCreditedAsync(provider);

            processed.Should().Be(IntegrationEventInboxResult.Processed);
            duplicate.Should().Be(IntegrationEventInboxResult.Duplicate);
            walletCreditProcessed.Should().Be(IntegrationEventInboxResult.Processed);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var statuses = await db.Payments.AsNoTracking()
                .Where(payment => payment.Id == first.Id || payment.Id == second.Id)
                .ToDictionaryAsync(payment => payment.Id, payment => payment.Status);
            statuses[first.Id].Should().Be(PaymentStatus.REFUNDED);
            statuses[second.Id].Should().Be(PaymentStatus.SUCCEEDED);
            (await db.WalletTransactions.AsNoTracking()
                .CountAsync(transaction =>
                    transaction.ReferenceType == WalletTransactionRef.BOOKING_REFUND
                    && transaction.ReferenceId == bookingId)).Should().Be(1);
            (await db.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "payment.wallet.credited")).Should().Be(1);
            var refunded = await db.OutboxEvents.AsNoTracking()
                .SingleAsync(row => row.EventType == "payment.payment.refunded");
            using var refundedPayload = JsonDocument.Parse(refunded.Payload);
            refundedPayload.RootElement.GetProperty("paymentId").GetGuid().Should().Be(first.Id);
            (await db.ProcessedIntegrationEvents.AsNoTracking()
                .CountAsync(row => row.EventId == producer.EventId)).Should().Be(1);
        });
    }

    [Fact]
    public async Task GenericCancellationWithAmbiguousFunding_ExactRefundDoesNotCrossCreditOtherPayment()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var bookingId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var walletPayment = CreateSucceededWalletPayment(
                PaymentReferenceType.BOOKING,
                bookingId,
                userId,
                [Allocation(bookingId, 350_000)],
                Now.AddMinutes(-10));
            var lateVnPayPayment = CreateSucceededPayment(
                PaymentReferenceType.BOOKING,
                bookingId,
                userId,
                [Allocation(bookingId, 350_000)],
                Now.AddMinutes(-20));
            await SeedAsync(
                connectionString,
                userId,
                walletPayment,
                lateVnPayPayment);

            await using (var timestampProvider = CreateProvider(connectionString))
            await using (var timestampScope = timestampProvider.CreateAsyncScope())
            {
                var db = timestampScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE vietride_payment.payments
                    SET created_at = {Now.AddDays(-2)}
                    WHERE id = {lateVnPayPayment.Id}
                    """);
            }

            await using var provider = CreateProvider(connectionString);
            var cancelled = new BookingCancelledIntegrationEvent
            {
                EventId = Guid.NewGuid(),
                OccurredAtOffset = Now,
                BookingId = bookingId,
                UserId = userId,
                RefundAmount = 350_000,
                RefundOverride = false,
                CancellationReason = "USER_INITIATED",
            };
            var cancelledPayload = JsonSerializer.Serialize(cancelled, JsonOptions);
            (await DeliverCancellationAsync(provider, cancelled, cancelledPayload))
                .Should().Be(IntegrationEventInboxResult.Processed);

            var exactEvent = ConsumerEvent(
                lateVnPayPayment,
                bookingId,
                userId,
                350_000);
            var exactPayload = JsonSerializer.Serialize(exactEvent, JsonOptions);
            (await DeliverAsync(provider, exactEvent, exactPayload))
                .Should().Be(IntegrationEventInboxResult.Processed);
            (await DeliverWalletCreditedAsync(
                    provider,
                    integrationEvent => integrationEvent.PaymentId == lateVnPayPayment.Id))
                .Should().Be(IntegrationEventInboxResult.Processed);

            await using (var retryScope = provider.CreateAsyncScope())
            {
                var job = ActivatorUtilities.CreateInstance<RefundFailureRetryJob>(
                    retryScope.ServiceProvider);
                await job.RunAsync();
            }
            (await DeliverCancellationAsync(provider, cancelled, cancelledPayload))
                .Should().Be(IntegrationEventInboxResult.Duplicate);
            (await DeliverAsync(provider, exactEvent, exactPayload))
                .Should().Be(IntegrationEventInboxResult.Duplicate);

            await using var assertScope = provider.CreateAsyncScope();
            var assertDb = assertScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await assertDb.Wallets.AsNoTracking().SingleAsync(row => row.UserId == userId))
                .Balance.Amount.Should().Be(350_000);
            (await assertDb.WalletTransactions.AsNoTracking()
                .CountAsync(transaction =>
                    transaction.ReferenceType == WalletTransactionRef.BOOKING_REFUND
                    && transaction.ReferenceId == bookingId)).Should().Be(1);
            var statuses = await assertDb.Payments.AsNoTracking()
                .Where(payment =>
                    payment.Id == walletPayment.Id
                    || payment.Id == lateVnPayPayment.Id)
                .ToDictionaryAsync(payment => payment.Id, payment => payment.Status);
            statuses[walletPayment.Id].Should().Be(PaymentStatus.SUCCEEDED);
            statuses[lateVnPayPayment.Id].Should().Be(PaymentStatus.REFUNDED);
            (await assertDb.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "payment.wallet.credited")).Should().Be(1);
            var refundedPaymentIds = await assertDb.OutboxEvents.AsNoTracking()
                .Where(row => row.EventType == "payment.payment.refunded")
                .Select(row => row.Payload)
                .ToListAsync();
            refundedPaymentIds
                .Select(GetRefundedPaymentId)
                .Should().BeEquivalentTo([lateVnPayPayment.Id]);
            var failure = await assertDb.RefundFailureLogs.AsNoTracking().SingleAsync();
            failure.ResolvedAt.Should().BeNull();
        });
    }

    [Fact]
    public async Task DistinctCapturedPaymentsForSameBooking_EachCreditExactlyOnceAndReplayAddsNothing()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var bookingId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var first = CreateSucceededPayment(
                PaymentReferenceType.BOOKING,
                bookingId,
                userId,
                [Allocation(bookingId, 350_000)]);
            var second = CreateSucceededPayment(
                PaymentReferenceType.BOOKING,
                bookingId,
                userId,
                [Allocation(bookingId, 350_000)]);
            await SeedAsync(connectionString, userId, first, second);

            var firstEvent = ConsumerEvent(first, bookingId, userId, 350_000);
            var secondEvent = ConsumerEvent(second, bookingId, userId, 350_000);
            var firstPayload = JsonSerializer.Serialize(firstEvent, JsonOptions);
            var secondPayload = JsonSerializer.Serialize(secondEvent, JsonOptions);
            await using var provider = CreateProvider(connectionString);

            (await DeliverAsync(provider, firstEvent, firstPayload))
                .Should().Be(IntegrationEventInboxResult.Processed);
            (await DeliverAsync(provider, secondEvent, secondPayload))
                .Should().Be(IntegrationEventInboxResult.Processed);
            (await DeliverAsync(provider, firstEvent, firstPayload))
                .Should().Be(IntegrationEventInboxResult.Duplicate);
            (await DeliverAsync(provider, secondEvent, secondPayload))
                .Should().Be(IntegrationEventInboxResult.Duplicate);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await db.Wallets.AsNoTracking().SingleAsync(row => row.UserId == userId))
                .Balance.Amount.Should().Be(700_000);
            (await db.WalletTransactions.AsNoTracking()
                .Where(transaction =>
                    transaction.ReferenceType == WalletTransactionRef.BOOKING_REFUND
                    && transaction.ReferenceId == bookingId)
                .ToListAsync()).Should().HaveCount(2)
                .And.OnlyHaveUniqueItems(transaction => transaction.Id)
                .And.OnlyContain(transaction => transaction.Amount.Amount == 350_000);
            (await db.Payments.AsNoTracking()
                .Where(payment => payment.Id == first.Id || payment.Id == second.Id)
                .Select(payment => payment.Status)
                .ToListAsync()).Should().OnlyContain(status => status == PaymentStatus.REFUNDED);
            (await db.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "payment.wallet.credited")).Should().Be(2);
            (await db.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "payment.payment.refunded")).Should().Be(2);
            (await db.ProcessedIntegrationEvents.AsNoTracking()
                .CountAsync(row =>
                    row.EventId == firstEvent.EventId
                    || row.EventId == secondEvent.EventId)).Should().Be(2);
        });
    }

    [Fact]
    public async Task ExistingPartialGenericRefund_ReplayDoesNotTopUpOrReconcileFundingPayment()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var bookingId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var payment = CreateSucceededWalletPayment(
                PaymentReferenceType.BOOKING,
                bookingId,
                userId,
                [Allocation(bookingId, 350_000)]);
            await SeedAsync(connectionString, userId, payment);
            await using (var seedProvider = CreateProvider(connectionString))
            await using (var seedScope = seedProvider.CreateAsyncScope())
            {
                var seedDb = seedScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                var wallet = await seedDb.Wallets.SingleAsync(row => row.UserId == userId);
                wallet.Credit(Money.FromRaw(175_000));
                seedDb.WalletTransactions.Add(WalletTransaction.CreateBookingRefundCredit(
                    userId,
                    bookingId,
                    Money.FromRaw(175_000),
                    Money.Zero,
                    Money.FromRaw(175_000)));
                await seedDb.SaveChangesAsync();
            }

            var cancelled = new BookingCancelledIntegrationEvent
            {
                EventId = Guid.NewGuid(),
                OccurredAtOffset = Now,
                BookingId = bookingId,
                UserId = userId,
                RefundAmount = 350_000,
                RefundOverride = false,
                CancellationReason = "USER_INITIATED",
            };
            var payload = JsonSerializer.Serialize(cancelled, JsonOptions);
            await using var provider = CreateProvider(connectionString);

            (await DeliverCancellationAsync(provider, cancelled, payload))
                .Should().Be(IntegrationEventInboxResult.Processed);
            (await DeliverCancellationAsync(provider, cancelled, payload))
                .Should().Be(IntegrationEventInboxResult.Duplicate);

            await using var assertScope = provider.CreateAsyncScope();
            var db = assertScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await db.Wallets.AsNoTracking().SingleAsync(row => row.UserId == userId))
                .Balance.Amount.Should().Be(175_000);
            (await db.WalletTransactions.AsNoTracking().CountAsync()).Should().Be(1);
            (await db.PlatformWalletTransactions.AsNoTracking().CountAsync()).Should().Be(0);
            (await db.OperatorLedgerEntries.AsNoTracking().CountAsync()).Should().Be(0);
            (await db.OutboxEvents.AsNoTracking().CountAsync()).Should().Be(0);
            (await db.RefundFailureLogs.AsNoTracking().CountAsync()).Should().Be(0);
            (await db.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                .Status.Should().Be(PaymentStatus.SUCCEEDED);
        });
    }

    [Fact]
    public async Task GenericFailureAfterStagedMutations_RollsBackToSavepointAndRetryCommitsExactlyOnce()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var bookingId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var payment = CreateSucceededWalletPayment(
                PaymentReferenceType.BOOKING,
                bookingId,
                userId,
                [Allocation(bookingId, 350_000)]);
            await SeedAsync(connectionString, userId, payment);
            var cancelled = new BookingCancelledIntegrationEvent
            {
                EventId = Guid.NewGuid(),
                OccurredAtOffset = Now,
                BookingId = bookingId,
                UserId = userId,
                RefundAmount = 350_000,
                RefundOverride = false,
                CancellationReason = "USER_INITIATED",
            };
            var payload = JsonSerializer.Serialize(cancelled, JsonOptions);

            await using (var failingProvider = CreateProvider(
                connectionString,
                failAfterWalletCreditedEnqueue: true))
            {
                (await DeliverCancellationAsync(failingProvider, cancelled, payload))
                    .Should().Be(IntegrationEventInboxResult.Processed);
            }

            await using (var assertProvider = CreateProvider(connectionString))
            await using (var assertScope = assertProvider.CreateAsyncScope())
            {
                var db = assertScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                (await db.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                    .Status.Should().Be(PaymentStatus.SUCCEEDED);
                (await db.Wallets.AsNoTracking().SingleAsync(row => row.UserId == userId))
                    .Balance.Amount.Should().Be(0);
                (await db.WalletTransactions.AsNoTracking().CountAsync()).Should().Be(0);
                (await db.PlatformWalletTransactions.AsNoTracking().CountAsync()).Should().Be(0);
                (await db.OperatorLedgerEntries.AsNoTracking().CountAsync()).Should().Be(0);
                (await db.OutboxEvents.AsNoTracking().CountAsync()).Should().Be(0);
                (await db.ProcessedIntegrationEvents.AsNoTracking().CountAsync()).Should().Be(1);
                var failure = await db.RefundFailureLogs.AsNoTracking().SingleAsync();
                failure.BookingId.Should().Be(bookingId);
                failure.ReferenceType.Should().Be("BOOKING_REFUND");
                failure.ReferenceId.Should().Be(bookingId);
                failure.ResolvedAt.Should().BeNull();
            }

            await using var retryProvider = CreateProvider(connectionString);
            await using (var retryScope = retryProvider.CreateAsyncScope())
            {
                var job = ActivatorUtilities.CreateInstance<RefundFailureRetryJob>(
                    retryScope.ServiceProvider);
                await job.RunAsync();
            }
            (await DeliverWalletCreditedAsync(
                    retryProvider,
                    integrationEvent => integrationEvent.PaymentId == payment.Id))
                .Should().Be(IntegrationEventInboxResult.Processed);
            (await DeliverCancellationAsync(retryProvider, cancelled, payload))
                .Should().Be(IntegrationEventInboxResult.Duplicate);

            await using var finalScope = retryProvider.CreateAsyncScope();
            var finalDb = finalScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await finalDb.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                .Status.Should().Be(PaymentStatus.REFUNDED);
            (await finalDb.Wallets.AsNoTracking().SingleAsync(row => row.UserId == userId))
                .Balance.Amount.Should().Be(350_000);
            (await finalDb.WalletTransactions.AsNoTracking().CountAsync()).Should().Be(1);
            (await finalDb.PlatformWalletTransactions.AsNoTracking().CountAsync()).Should().Be(1);
            (await finalDb.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "payment.wallet.credited")).Should().Be(1);
            (await finalDb.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "payment.payment.refunded")).Should().Be(1);
            (await finalDb.RefundFailureLogs.AsNoTracking().SingleAsync())
                .ResolvedAt.Should().Be(Now);
        });
    }

    [Fact]
    public async Task FailureAfterWalletCreditStaging_RollsBackEverythingAndRetryKeepsExactPaymentCorrelation()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var bookingId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var payment = CreateSucceededPayment(
                PaymentReferenceType.BOOKING,
                bookingId,
                userId,
                [Allocation(bookingId, 350_000)]);
            var laterAttempt = CreateSucceededPayment(
                PaymentReferenceType.BOOKING,
                bookingId,
                userId,
                [Allocation(bookingId, 350_000)]);
            await SeedAsync(connectionString, userId, payment, laterAttempt);
            var integrationEvent = ConsumerEvent(payment, bookingId, userId, 350_000);
            var payload = JsonSerializer.Serialize(integrationEvent, JsonOptions);

            await using (var failingProvider = CreateProvider(
                connectionString,
                failAfterPaymentRefundedEnqueue: true))
            {
                (await DeliverAsync(failingProvider, integrationEvent, payload))
                    .Should().Be(IntegrationEventInboxResult.Processed);
            }

            await using (var assertProvider = CreateProvider(connectionString))
            await using (var assertScope = assertProvider.CreateAsyncScope())
            {
                var db = assertScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                (await db.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                    .Status.Should().Be(PaymentStatus.SUCCEEDED);
                (await db.Payments.AsNoTracking().SingleAsync(row => row.Id == laterAttempt.Id))
                    .Status.Should().Be(PaymentStatus.SUCCEEDED);
                (await db.Wallets.AsNoTracking().SingleAsync(row => row.UserId == userId))
                    .Balance.Amount.Should().Be(0);
                (await db.WalletTransactions.AsNoTracking().CountAsync()).Should().Be(0);
                (await db.PlatformWalletTransactions.AsNoTracking().CountAsync()).Should().Be(0);
                (await db.OperatorLedgerEntries.AsNoTracking().CountAsync()).Should().Be(0);
                (await db.OutboxEvents.AsNoTracking().CountAsync()).Should().Be(0);
                (await db.ProcessedIntegrationEvents.AsNoTracking().CountAsync()).Should().Be(1);
                var failure = await db.RefundFailureLogs.AsNoTracking().SingleAsync();
                failure.BookingId.Should().Be(bookingId);
                failure.UserId.Should().Be(userId);
                failure.Amount.Should().Be(350_000);
                failure.ReferenceType.Should().Be("BOOKING_REFUND_PAYMENT");
                failure.ReferenceId.Should().Be(payment.Id);
                failure.RetryCount.Should().Be(0);
                failure.ResolvedAt.Should().BeNull();
            }

            await using var retryProvider = CreateProvider(connectionString);
            await using (var jobScope = retryProvider.CreateAsyncScope())
            {
                var job = ActivatorUtilities.CreateInstance<RefundFailureRetryJob>(
                    jobScope.ServiceProvider);
                await job.RunAsync();
            }

            (await DeliverAsync(retryProvider, integrationEvent, payload))
                .Should().Be(IntegrationEventInboxResult.Duplicate);
            await using var retryAssertScope = retryProvider.CreateAsyncScope();
            var retryDb = retryAssertScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await retryDb.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                .Status.Should().Be(PaymentStatus.REFUNDED);
            (await retryDb.Payments.AsNoTracking().SingleAsync(row => row.Id == laterAttempt.Id))
                .Status.Should().Be(PaymentStatus.SUCCEEDED);
            (await retryDb.WalletTransactions.AsNoTracking().CountAsync()).Should().Be(1);
            (await retryDb.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "payment.payment.refunded")).Should().Be(1);
            var resolvedFailure = await retryDb.RefundFailureLogs.AsNoTracking().SingleAsync();
            resolvedFailure.ResolvedAt.Should().Be(Now);
            resolvedFailure.RetryCount.Should().Be(0);
        });
    }

    [Fact]
    public async Task HistoricalPartialRefund_DoesNotSatisfyExactGroupAllocations()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var firstBookingId = Guid.NewGuid();
            var secondBookingId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var payment = CreateSucceededPayment(
                PaymentReferenceType.BOOKING_GROUP,
                Guid.NewGuid(),
                userId,
                [
                    Allocation(firstBookingId, 200_000),
                    Allocation(secondBookingId, 300_000),
                ]);
            await SeedAsync(connectionString, userId, payment);
            await using (var partialProvider = CreateProvider(connectionString))
            await using (var partialScope = partialProvider.CreateAsyncScope())
            {
                var db = partialScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                var wallet = await db.Wallets.SingleAsync(row => row.UserId == userId);
                wallet.Credit(Money.FromRaw(100_000));
                db.WalletTransactions.Add(WalletTransaction.CreateBookingRefundCredit(
                    userId,
                    firstBookingId,
                    Money.FromRaw(100_000),
                    Money.Zero,
                    Money.FromRaw(100_000)));
                await db.SaveChangesAsync();
            }

            var firstEvent = ConsumerEvent(payment, firstBookingId, userId, 200_000);
            var firstPayload = JsonSerializer.Serialize(firstEvent, JsonOptions);
            await using var provider = CreateProvider(connectionString);
            (await DeliverAsync(provider, firstEvent, firstPayload))
                .Should().Be(IntegrationEventInboxResult.Processed);
            (await DeliverAsync(provider, firstEvent, firstPayload))
                .Should().Be(IntegrationEventInboxResult.Duplicate);

            await using (var firstScope = provider.CreateAsyncScope())
            {
                var db = firstScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                (await db.WalletTransactions.AsNoTracking()
                    .Where(transaction =>
                        transaction.ReferenceType == WalletTransactionRef.BOOKING_REFUND
                        && transaction.ReferenceId == firstBookingId)
                    .Select(transaction => transaction.Amount)
                    .ToListAsync()).Sum(amount => amount.Amount).Should().Be(200_000);
                (await db.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                    .Status.Should().Be(PaymentStatus.SUCCEEDED);
                (await db.OutboxEvents.AsNoTracking()
                    .CountAsync(row => row.EventType == "payment.payment.refunded")).Should().Be(0);
            }

            var secondEvent = ConsumerEvent(payment, secondBookingId, userId, 300_000);
            var secondPayload = JsonSerializer.Serialize(secondEvent, JsonOptions);
            (await DeliverAsync(provider, secondEvent, secondPayload))
                .Should().Be(IntegrationEventInboxResult.Processed);

            await using var finalScope = provider.CreateAsyncScope();
            var finalDb = finalScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await finalDb.Wallets.AsNoTracking().SingleAsync(row => row.UserId == userId))
                .Balance.Amount.Should().Be(500_000);
            (await finalDb.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                .Status.Should().Be(PaymentStatus.REFUNDED);
            (await finalDb.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "payment.payment.refunded")).Should().Be(1);
        });
    }

    [Fact]
    public async Task ConcurrentGroupAllocationRefunds_CreditBothLegsAndPublishSinglePaymentRefundedEvent()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var firstBookingId = Guid.NewGuid();
            var secondBookingId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var payment = CreateSucceededPayment(
                PaymentReferenceType.BOOKING_GROUP,
                Guid.NewGuid(),
                userId,
                [
                    Allocation(firstBookingId, 200_000),
                    Allocation(secondBookingId, 300_000),
                ]);
            await SeedAsync(connectionString, userId, payment);
            var firstEvent = ConsumerEvent(payment, firstBookingId, userId, 200_000);
            var secondEvent = ConsumerEvent(payment, secondBookingId, userId, 300_000);

            await using var provider = CreateProvider(connectionString);
            var deliveries = await Task.WhenAll(
                DeliverAsync(provider, firstEvent, JsonSerializer.Serialize(firstEvent, JsonOptions)),
                DeliverAsync(provider, secondEvent, JsonSerializer.Serialize(secondEvent, JsonOptions)));

            deliveries.Should().OnlyContain(result => result == IntegrationEventInboxResult.Processed);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await db.Wallets.AsNoTracking().SingleAsync(row => row.UserId == userId))
                .Balance.Amount.Should().Be(500_000);
            (await db.WalletTransactions.AsNoTracking()
                .CountAsync(transaction =>
                    transaction.ReferenceType == WalletTransactionRef.BOOKING_REFUND
                    && (transaction.ReferenceId == firstBookingId
                        || transaction.ReferenceId == secondBookingId))).Should().Be(2);
            (await db.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                .Status.Should().Be(PaymentStatus.REFUNDED);
            (await db.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "payment.payment.refunded")).Should().Be(1);
            (await db.ProcessedIntegrationEvents.AsNoTracking().CountAsync()).Should().Be(2);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExactZeroAndPositiveGroupRefunds_InEitherOrder_ReverseVoucherAndCompleteOnce(
        bool positiveFirst)
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var zeroBookingId = Guid.NewGuid();
            var positiveBookingId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var zeroAllocation = Allocation(
                zeroBookingId,
                grossAmount: 100_000,
                vietRideVoucherAmount: 100_000);
            var payment = CreateSucceededPayment(
                PaymentReferenceType.BOOKING_GROUP,
                groupId,
                userId,
                [
                    zeroAllocation,
                    Allocation(positiveBookingId, 300_000),
                ]);
            await SeedAsync(connectionString, userId, payment);
            await using (var seedProvider = CreateProvider(connectionString))
            await using (var seedScope = seedProvider.CreateAsyncScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                db.OperatorLedgerEntries.Add(OperatorLedgerEntry.Create(
                    zeroAllocation.OperatorId,
                    zeroAllocation.TripId,
                    OperatorLedgerEntryType.VOUCHER_VIETRIDE_FUNDED_CREDIT,
                    100_000,
                    OperatorLedgerReferenceType.BOOKING,
                    zeroBookingId,
                    Guid.NewGuid()));
                await db.SaveChangesAsync();
            }

            var zeroEvent = ConsumerEvent(payment, zeroBookingId, userId, amount: 0);
            var positiveEvent = ConsumerEvent(payment, positiveBookingId, userId, 300_000);
            var zeroPayload = JsonSerializer.Serialize(zeroEvent, JsonOptions);
            var positivePayload = JsonSerializer.Serialize(positiveEvent, JsonOptions);
            await using var provider = CreateProvider(connectionString);

            var firstEvent = positiveFirst ? positiveEvent : zeroEvent;
            var firstPayload = positiveFirst ? positivePayload : zeroPayload;
            (await DeliverAsync(provider, firstEvent, firstPayload))
                .Should().Be(IntegrationEventInboxResult.Processed);

            await using (var intermediateScope = provider.CreateAsyncScope())
            {
                var db = intermediateScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                (await db.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                    .Status.Should().Be(PaymentStatus.SUCCEEDED);
                if (positiveFirst)
                {
                    (await db.OperatorLedgerEntries.AsNoTracking().CountAsync(row =>
                        row.EntryType == OperatorLedgerEntryType.ADJUSTMENT
                        && row.ReferenceId == zeroBookingId)).Should().Be(0);
                }
            }

            var secondEvent = positiveFirst ? zeroEvent : positiveEvent;
            var secondPayload = positiveFirst ? zeroPayload : positivePayload;
            (await DeliverAsync(provider, secondEvent, secondPayload))
                .Should().Be(IntegrationEventInboxResult.Processed);
            (await DeliverAsync(provider, zeroEvent, zeroPayload))
                .Should().Be(IntegrationEventInboxResult.Duplicate);
            (await DeliverAsync(provider, positiveEvent, positivePayload))
                .Should().Be(IntegrationEventInboxResult.Duplicate);

            await using var assertScope = provider.CreateAsyncScope();
            var assertDb = assertScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await assertDb.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                .Status.Should().Be(PaymentStatus.REFUNDED);
            (await assertDb.Wallets.AsNoTracking().SingleAsync(row => row.UserId == userId))
                .Balance.Amount.Should().Be(300_000);
            (await assertDb.WalletTransactions.AsNoTracking()
                .CountAsync(row => row.ReferenceType == WalletTransactionRef.BOOKING_REFUND))
                .Should().Be(1);
            (await assertDb.PlatformWalletTransactions.AsNoTracking()
                .CountAsync(row => row.ReferenceType == PlatformWalletTransactionRef.BOOKING_REFUND))
                .Should().Be(1);
            var refundRows = await assertDb.OperatorLedgerEntries.AsNoTracking()
                .Where(row => row.EntryType == OperatorLedgerEntryType.BOOKING_REFUND)
                .ToListAsync();
            refundRows.Should().ContainSingle()
                .Which.Amount.Should().Be(-300_000);
            var adjustmentRows = await assertDb.OperatorLedgerEntries.AsNoTracking()
                .Where(row => row.EntryType == OperatorLedgerEntryType.ADJUSTMENT
                    && row.ReferenceId == zeroBookingId)
                .ToListAsync();
            adjustmentRows.Should().ContainSingle()
                .Which.Amount.Should().Be(-100_000);
            (await assertDb.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "payment.wallet.credited")).Should().Be(1);
            (await assertDb.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "payment.payment.refunded")).Should().Be(1);
        });
    }

    [Fact]
    public async Task ExactZeroRefundFailure_PersistsZeroPayloadAndRetryReversesVoucherOnce()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var zeroBookingId = Guid.NewGuid();
            var positiveBookingId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var payment = CreateSucceededPayment(
                PaymentReferenceType.BOOKING_GROUP,
                Guid.NewGuid(),
                userId,
                [
                    Allocation(
                        zeroBookingId,
                        grossAmount: 100_000,
                        vietRideVoucherAmount: 100_000),
                    Allocation(positiveBookingId, 300_000),
                ]);
            await SeedAsync(connectionString, userId, payment);
            var integrationEvent = ConsumerEvent(payment, zeroBookingId, userId, amount: 0);
            var payload = JsonSerializer.Serialize(integrationEvent, JsonOptions);

            await using (var failingProvider = CreateProvider(
                connectionString,
                failAfterRefundLedgerWrite: true))
            {
                (await DeliverAsync(failingProvider, integrationEvent, payload))
                    .Should().Be(IntegrationEventInboxResult.Processed);
            }

            await using (var failedProvider = CreateProvider(connectionString))
            await using (var failedScope = failedProvider.CreateAsyncScope())
            {
                var db = failedScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                (await db.OperatorLedgerEntries.AsNoTracking().CountAsync()).Should().Be(0);
                var failure = await db.RefundFailureLogs.AsNoTracking().SingleAsync();
                failure.Amount.Should().Be(0);
                failure.ReferenceType.Should().Be("BOOKING_REFUND_PAYMENT");
                failure.ReferenceId.Should().Be(payment.Id);
                failure.ResolvedAt.Should().BeNull();
            }

            await using var retryProvider = CreateProvider(connectionString);
            await using (var retryScope = retryProvider.CreateAsyncScope())
            {
                var job = ActivatorUtilities.CreateInstance<RefundFailureRetryJob>(
                    retryScope.ServiceProvider);
                await job.RunAsync();
            }

            await using var assertScope = retryProvider.CreateAsyncScope();
            var assertDb = assertScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await assertDb.OperatorLedgerEntries.AsNoTracking()
                .CountAsync(row => row.EntryType == OperatorLedgerEntryType.ADJUSTMENT
                    && row.ReferenceId == zeroBookingId
                    && row.Amount == -100_000)).Should().Be(1);
            (await assertDb.WalletTransactions.AsNoTracking().CountAsync()).Should().Be(0);
            (await assertDb.PlatformWalletTransactions.AsNoTracking().CountAsync()).Should().Be(0);
            (await assertDb.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                .Status.Should().Be(PaymentStatus.SUCCEEDED);
            (await assertDb.RefundFailureLogs.AsNoTracking().SingleAsync())
                .ResolvedAt.Should().Be(Now);
        });
    }

    [Fact]
    public async Task GenericBookingGroup_RequiresEveryGenericLegAndIgnoresExactDeterministicRows()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var firstBookingId = Guid.NewGuid();
            var secondBookingId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var payment = CreateSucceededPayment(
                PaymentReferenceType.BOOKING_GROUP,
                Guid.NewGuid(),
                userId,
                [
                    Allocation(firstBookingId, 200_000),
                    Allocation(secondBookingId, 300_000),
                ]);
            await SeedAsync(connectionString, userId, payment);
            await using (var seedProvider = CreateProvider(connectionString))
            await using (var seedScope = seedProvider.CreateAsyncScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                var wallet = await db.Wallets.SingleAsync(row => row.UserId == userId);
                wallet.Credit(Money.FromRaw(300_000));
                db.WalletTransactions.Add(WalletTransaction.CreateBookingRefundCredit(
                    CreateExactBookingRefundTransactionId(payment.Id, secondBookingId),
                    userId,
                    secondBookingId,
                    Money.FromRaw(300_000),
                    Money.Zero,
                    Money.FromRaw(300_000)));
                await db.SaveChangesAsync();
            }

            await using var provider = CreateProvider(connectionString);
            var firstCancellation = new BookingCancelledIntegrationEvent
            {
                EventId = Guid.NewGuid(),
                OccurredAtOffset = Now,
                BookingId = firstBookingId,
                UserId = userId,
                RefundAmount = 200_000,
                RefundOverride = false,
                CancellationReason = "USER_INITIATED",
            };
            (await DeliverCancellationAsync(
                    provider,
                    firstCancellation,
                    JsonSerializer.Serialize(firstCancellation, JsonOptions)))
                .Should().Be(IntegrationEventInboxResult.Processed);
            (await DeliverWalletCreditedAsync(
                    provider,
                    integrationEvent => integrationEvent.ReferenceId == firstBookingId))
                .Should().Be(IntegrationEventInboxResult.Processed);

            await using (var firstScope = provider.CreateAsyncScope())
            {
                var db = firstScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                (await db.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                    .Status.Should().Be(PaymentStatus.SUCCEEDED);
                (await db.OutboxEvents.AsNoTracking()
                    .CountAsync(row => row.EventType == "payment.payment.refunded")).Should().Be(0);
            }

            var secondCancellation = new BookingCancelledIntegrationEvent
            {
                EventId = Guid.NewGuid(),
                OccurredAtOffset = Now,
                BookingId = secondBookingId,
                UserId = userId,
                RefundAmount = 300_000,
                RefundOverride = false,
                CancellationReason = "USER_INITIATED",
            };
            (await DeliverCancellationAsync(
                    provider,
                    secondCancellation,
                    JsonSerializer.Serialize(secondCancellation, JsonOptions)))
                .Should().Be(IntegrationEventInboxResult.Processed);
            await using var assertScope = provider.CreateAsyncScope();
            var assertDb = assertScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await assertDb.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                .Status.Should().Be(PaymentStatus.SUCCEEDED);
            (await assertDb.WalletTransactions.AsNoTracking().CountAsync()).Should().Be(2);
            (await assertDb.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "payment.payment.refunded")).Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenericAndExactSameFundingAllocation_InEitherOrder_CapMoneyAndVoucherReversal(
        bool exactFirst)
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var bookingId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var payment = CreateSucceededPayment(
                PaymentReferenceType.BOOKING,
                bookingId,
                userId,
                [Allocation(bookingId, 500_000, vietRideVoucherAmount: 100_000)]);
            await SeedAsync(connectionString, userId, payment);
            var exact = ConsumerEvent(payment, bookingId, userId, 400_000);
            var cancelled = CancellationEvent(bookingId, userId, 400_000);
            await using var provider = CreateProvider(connectionString);

            if (exactFirst)
            {
                (await DeliverAsync(provider, exact, JsonSerializer.Serialize(exact, JsonOptions)))
                    .Should().Be(IntegrationEventInboxResult.Processed);
                (await DeliverCancellationAsync(provider, cancelled, JsonSerializer.Serialize(cancelled, JsonOptions)))
                    .Should().Be(IntegrationEventInboxResult.Processed);
            }
            else
            {
                (await DeliverCancellationAsync(provider, cancelled, JsonSerializer.Serialize(cancelled, JsonOptions)))
                    .Should().Be(IntegrationEventInboxResult.Processed);
                (await DeliverAsync(provider, exact, JsonSerializer.Serialize(exact, JsonOptions)))
                    .Should().Be(IntegrationEventInboxResult.Processed);
            }

            await AssertCrossPhaseCapAsync(provider, payment.Id, bookingId, userId);
        });
    }

    [Fact]
    public async Task GenericAndExactSameFundingAllocation_ConcurrentDelivery_CapsMoneyAndVoucherReversal()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var bookingId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var payment = CreateSucceededPayment(
                PaymentReferenceType.BOOKING,
                bookingId,
                userId,
                [Allocation(bookingId, 500_000, vietRideVoucherAmount: 100_000)]);
            await SeedAsync(connectionString, userId, payment);
            var exact = ConsumerEvent(payment, bookingId, userId, 400_000);
            var cancelled = CancellationEvent(bookingId, userId, 400_000);
            await using var provider = CreateProvider(connectionString);

            var results = await Task.WhenAll(
                DeliverAsync(provider, exact, JsonSerializer.Serialize(exact, JsonOptions)),
                DeliverCancellationAsync(provider, cancelled, JsonSerializer.Serialize(cancelled, JsonOptions)));
            results.Should().OnlyContain(result => result == IntegrationEventInboxResult.Processed);

            await AssertCrossPhaseCapAsync(provider, payment.Id, bookingId, userId);
        });
    }

    [Fact]
    public async Task MixedOwnerRefundHistory_IsQuarantinedWithoutCreditingAuthoritativeOwner()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var bookingId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();
            var payment = CreateSucceededPayment(
                PaymentReferenceType.BOOKING,
                bookingId,
                userId,
                [Allocation(bookingId, 350_000)]);
            await SeedAsync(connectionString, userId, payment);
            await using (var seedProvider = CreateProvider(connectionString))
            await using (var seedScope = seedProvider.CreateAsyncScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                var otherWallet = Wallet.Create(otherUserId);
                otherWallet.Credit(Money.FromRaw(100_000));
                db.Wallets.Add(otherWallet);
                db.WalletTransactions.Add(WalletTransaction.CreateBookingRefundCredit(
                    otherUserId,
                    bookingId,
                    Money.FromRaw(100_000),
                    Money.Zero,
                    Money.FromRaw(100_000)));
                await db.SaveChangesAsync();
            }

            var integrationEvent = ConsumerEvent(payment, bookingId, userId, 350_000);
            var payload = JsonSerializer.Serialize(integrationEvent, JsonOptions);
            await using var provider = CreateProvider(connectionString);
            (await DeliverAsync(provider, integrationEvent, payload))
                .Should().Be(IntegrationEventInboxResult.Processed);

            await using var scope = provider.CreateAsyncScope();
            var assertDb = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await assertDb.Wallets.AsNoTracking().SingleAsync(row => row.UserId == userId))
                .Balance.Amount.Should().Be(0);
            (await assertDb.WalletTransactions.AsNoTracking().CountAsync()).Should().Be(1);
            (await assertDb.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                .Status.Should().Be(PaymentStatus.SUCCEEDED);
            (await assertDb.OutboxEvents.AsNoTracking().CountAsync()).Should().Be(0);
            (await assertDb.ProcessedIntegrationEvents.AsNoTracking().CountAsync()).Should().Be(1);
            var failure = await assertDb.RefundFailureLogs.AsNoTracking().SingleAsync();
            failure.ReferenceType.Should().Be("BOOKING_REFUND_PAYMENT");
            failure.ReferenceId.Should().Be(payment.Id);
            failure.RetryCount.Should().Be(0);
            failure.ResolvedAt.Should().BeNull();
            failure.FailureReason.Should().Contain("different wallet");
        });
    }

    [Fact]
    public async Task OwnerOrAmountMismatch_IsRejectedWithoutDurableMutation()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var bookingId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var payment = CreateSucceededPayment(
                PaymentReferenceType.BOOKING,
                bookingId,
                userId,
                [Allocation(bookingId, 350_000)]);
            await SeedAsync(connectionString, userId, payment);
            var invalid = ConsumerEvent(payment, bookingId, Guid.NewGuid(), 1);
            var payload = JsonSerializer.Serialize(invalid, JsonOptions);
            await using var provider = CreateProvider(connectionString);

            var action = () => DeliverAsync(provider, invalid, payload);
            await action.Should().ThrowAsync<ArgumentException>();

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await db.Payments.AsNoTracking().SingleAsync(row => row.Id == payment.Id))
                .Status.Should().Be(PaymentStatus.SUCCEEDED);
            (await db.WalletTransactions.AsNoTracking().CountAsync()).Should().Be(0);
            (await db.OutboxEvents.AsNoTracking().CountAsync()).Should().Be(0);
            (await db.ProcessedIntegrationEvents.AsNoTracking().CountAsync()).Should().Be(0);
        });
    }

    [Fact]
    public void AddInfrastructure_BindsCapturedPaymentRefundConsumer()
    {
        var configuration = Configuration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVietRideMessaging(configuration);
        services.AddInfrastructure(configuration, registerConsumers: true);

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<RabbitMqConsumerOptions<
                VietRide.Payment.Infrastructure.Messaging.BookingPaymentRefundRequestedIntegrationEvent>>>()
            .Value.Value;

        options.QueueName.Should().Be("payment.booking-payment-refund-requested");
        options.BindingKeys.Should().ContainSingle()
            .Which.Should().Be("booking.payment_refund.requested");
    }

    private static async Task<IntegrationEventInboxResult> DeliverAsync(
        ServiceProvider provider,
        VietRide.Payment.Infrastructure.Messaging.BookingPaymentRefundRequestedIntegrationEvent integrationEvent,
        string payload)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<PaymentDbContext>();
        var handler = new BookingPaymentRefundRequestedIntegrationEventHandler(
            services.GetRequiredService<IPaymentRepository>(),
            services.GetRequiredService<RefundRetryService>(),
            NullLogger<BookingPaymentRefundRequestedIntegrationEventHandler>.Instance);
        var inbox = services.GetRequiredService<IIntegrationEventInbox>();
        return await inbox.ExecuteAsync(
            "payment.booking-payment-refund-requested",
            integrationEvent.EventId,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant(),
            ct => handler.HandleAsync(integrationEvent, ct),
            CancellationToken.None);
    }

    private static async Task<IntegrationEventInboxResult> DeliverCancellationAsync(
        ServiceProvider provider,
        BookingCancelledIntegrationEvent integrationEvent,
        string payload)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var handler = new BookingCancelledIntegrationEventHandler(
            services.GetRequiredService<MediatR.ISender>(),
            NullLogger<BookingCancelledIntegrationEventHandler>.Instance,
            services);
        var inbox = services.GetRequiredService<IIntegrationEventInbox>();
        return await inbox.ExecuteAsync(
            "payment.booking-cancelled",
            integrationEvent.EventId!.Value,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant(),
            ct => handler.HandleAsync(integrationEvent, ct),
            CancellationToken.None);
    }

    private static async Task<IntegrationEventInboxResult> DeliverWalletCreditedAsync(
        ServiceProvider provider,
        Func<WalletCreditedConsumerEvent, bool>? predicate = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<PaymentDbContext>();
        var rows = await db.OutboxEvents.AsNoTracking()
            .Where(outbox => outbox.EventType == WalletCreditedConsumerEvent.EventType)
            .ToListAsync();
        var candidates = rows
            .Select(row => new
            {
                Row = row,
                Event = JsonSerializer.Deserialize<WalletCreditedConsumerEvent>(
                    row.Payload,
                    JsonOptions),
            })
            .Where(candidate => candidate.Event is not null
                && (predicate is null || predicate(candidate.Event)))
            .ToList();
        candidates.Should().ContainSingle();
        var row = candidates.Single().Row;
        var integrationEvent = candidates.Single().Event!;
        var handler = new MarkPaymentRefundedCommandHandler(
            services.GetRequiredService<IPaymentRepository>(),
            services.GetRequiredService<IWalletRepository>(),
            services.GetRequiredService<IClock>(),
            services.GetRequiredService<IIntegrationEventOutbox>(),
            NullLogger<MarkPaymentRefundedCommandHandler>.Instance);
        var inbox = services.GetRequiredService<IIntegrationEventInbox>();
        return await inbox.ExecuteAsync(
            "payment.payment-refunded",
            integrationEvent!.EventId,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(row.Payload))).ToLowerInvariant(),
            ct => handler.HandleAsync(integrationEvent, ct),
            CancellationToken.None);
    }

    private static async Task SeedAsync(
        string connectionString,
        Guid userId,
        params VietRide.Payment.Domain.Entities.Payment[] payments)
    {
        await using var provider = CreateProvider(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await db.Database.MigrateAsync();
        db.Wallets.Add(Wallet.Create(userId));
        var platformWallet = PlatformWallet.Create();
        platformWallet.Credit(Money.FromRaw(10_000_000));
        db.PlatformWallets.Add(platformWallet);
        db.Payments.AddRange(payments);
        await db.SaveChangesAsync();
    }

    private static ServiceProvider CreateProvider(
        string connectionString,
        bool failAfterPaymentRefundedEnqueue = false,
        bool failAfterWalletCreditedEnqueue = false,
        bool failAfterRefundLedgerWrite = false)
    {
        var configuration = Configuration(connectionString);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new FrozenClock());
        services.AddVietRideDbContext<PaymentDbContext>(
            configuration,
            configureDataSource: PaymentDbContext.ConfigurePostgresTypes,
            configureDbContext: options => options.ConfigureWarnings(
                warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddVietRideMediatRBehaviors(
            handlerAssemblies: [typeof(ApplicationAssemblyMarker).Assembly]);
        services.AddInfrastructure(configuration, registerConsumers: false);
        if (failAfterPaymentRefundedEnqueue)
        {
            services.RemoveAll<IIntegrationEventOutbox>();
            services.AddScoped<IIntegrationEventOutbox, FailingPaymentRefundedOutbox>();
        }
        else if (failAfterWalletCreditedEnqueue)
        {
            services.RemoveAll<IIntegrationEventOutbox>();
            services.AddScoped<IIntegrationEventOutbox, FailingWalletCreditedOutbox>();
        }
        if (failAfterRefundLedgerWrite)
        {
            services.RemoveAll<IRevenueLedgerWriter>();
            services.AddScoped<IRevenueLedgerWriter, FailingRefundRevenueLedgerWriter>();
        }

        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(string? connectionString = null)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString
                    ?? "Host=localhost;Port=5432;Database=vietride_payment;Username=vietride;Password=vietride_dev",
                ["REDIS_URL"] = "localhost:6379",
                ["RabbitMq:ExchangeName"] = "vietride.events",
                ["InvoiceStorage:Provider"] = "E2E_LOCAL",
                ["VNPAY_BASE_URL"] = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
                ["VNPAY_RETURN_URL"] = "https://example.test/vnpay-return",
                ["VNPAY_IPN_URL"] = "https://example.test/v1/payments/vnpay-ipn",
            })
            .Build();

    private static PaymentAllocationV1 Allocation(Guid bookingId, long amount)
        => Allocation(
            bookingId,
            amount,
            vietRideVoucherAmount: 0,
            operatorVoucherAmount: 0);

    private static BookingCancelledIntegrationEvent CancellationEvent(
        Guid bookingId,
        Guid userId,
        long refundAmount)
        => new()
        {
            EventId = Guid.NewGuid(),
            OccurredAtOffset = Now,
            BookingId = bookingId,
            UserId = userId,
            RefundAmount = refundAmount,
            RefundOverride = false,
            CancellationReason = "USER_INITIATED",
        };

    private static async Task AssertCrossPhaseCapAsync(
        ServiceProvider provider,
        Guid paymentId,
        Guid bookingId,
        Guid userId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        (await db.Wallets.AsNoTracking().SingleAsync(row => row.UserId == userId))
            .Balance.Amount.Should().Be(400_000);
        (await db.WalletTransactions.AsNoTracking().CountAsync(row =>
            row.ReferenceType == WalletTransactionRef.BOOKING_REFUND
            && row.ReferenceId == bookingId)).Should().Be(1);
        (await db.PlatformWalletTransactions.AsNoTracking().CountAsync(row =>
            row.ReferenceId == bookingId)).Should().Be(1);
        (await db.OperatorLedgerEntries.AsNoTracking().CountAsync(row =>
            row.EntryType == OperatorLedgerEntryType.ADJUSTMENT
            && row.ReferenceId == bookingId
            && row.Amount == -100_000)).Should().Be(1);
        (await db.OperatorLedgerEntries.AsNoTracking().Where(row =>
                row.ReferenceId == bookingId)
            .SumAsync(row => row.Amount)).Should().Be(-500_000);
        (await db.Payments.AsNoTracking().SingleAsync(row => row.Id == paymentId))
            .Status.Should().Be(PaymentStatus.REFUNDED);
        (await db.OutboxEvents.AsNoTracking().CountAsync(row =>
            row.EventType == "payment.payment.refunded")).Should().Be(1);
    }

    private static PaymentAllocationV1 Allocation(
        Guid bookingId,
        long grossAmount,
        long vietRideVoucherAmount,
        long operatorVoucherAmount = 0)
        => new(
            bookingId,
            "BOOKING",
            Guid.NewGuid(),
            Guid.NewGuid(),
            grossAmount,
            vietRideVoucherAmount,
            operatorVoucherAmount);

    private static VietRide.Payment.Domain.Entities.Payment CreateSucceededPayment(
        PaymentReferenceType referenceType,
        Guid referenceId,
        Guid userId,
        IReadOnlyList<PaymentAllocationV1> allocations,
        DateTimeOffset? succeededAt = null)
    {
        var amount = allocations.Sum(allocation =>
            allocation.GrossAmount
            - allocation.VoucherVietRideFundedAmount
            - allocation.VoucherOperatorFundedAmount);
        var context = new PaymentContextV1(1, allocations);
        var payment = VietRide.Payment.Domain.Entities.Payment.CreatePendingRedirectVnPay(
            referenceType,
            referenceId,
            userId,
            Money.FromRaw(amount),
            $"txn-{Guid.NewGuid():N}",
            Guid.NewGuid().ToString("D"),
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
            Now.AddMinutes(10));
        payment.AttachContext(PaymentContextCodec.ValidateAndSerialize(
            context,
            referenceType.ToString(),
            referenceId,
            amount));
        payment.MarkSucceeded("00", succeededAt ?? Now.AddMinutes(-1));
        return payment;
    }

    private static VietRide.Payment.Domain.Entities.Payment CreateSucceededWalletPayment(
        PaymentReferenceType referenceType,
        Guid referenceId,
        Guid userId,
        IReadOnlyList<PaymentAllocationV1> allocations,
        DateTimeOffset? succeededAt = null)
    {
        var amount = allocations.Sum(allocation =>
            allocation.GrossAmount
            - allocation.VoucherVietRideFundedAmount
            - allocation.VoucherOperatorFundedAmount);
        var payment = VietRide.Payment.Domain.Entities.Payment.CreateSucceededWalletCharge(
            referenceType,
            referenceId,
            userId,
            Money.FromRaw(amount),
            succeededAt ?? Now.AddMinutes(-1));
        payment.AttachContext(PaymentContextCodec.ValidateAndSerialize(
            new PaymentContextV1(1, allocations),
            referenceType.ToString(),
            referenceId,
            amount));
        return payment;
    }

    private static VietRide.Payment.Infrastructure.Messaging.BookingPaymentRefundRequestedIntegrationEvent ConsumerEvent(
        VietRide.Payment.Domain.Entities.Payment payment,
        Guid bookingId,
        Guid userId,
        long amount)
        => new()
        {
            EventId = Guid.NewGuid(),
            OccurredAtOffset = Now,
            PaymentId = payment.Id,
            PaymentReferenceType = payment.ReferenceType.ToString(),
            PaymentReferenceId = payment.ReferenceId,
            BookingId = bookingId,
            UserId = userId,
            Amount = amount,
            Reason = "PAYMENT_CAPTURE_AFTER_BOOKING_EXPIRY",
        };

    private static Guid GetRefundedPaymentId(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("paymentId").GetGuid();
    }

    private static Guid CreateExactBookingRefundTransactionId(
        Guid paymentId,
        Guid bookingId)
    {
        var correlation = System.Text.Encoding.UTF8.GetBytes(
            $"booking-refund-payment:{paymentId:D}:allocation:{bookingId:D}");
        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(correlation, hash);
        var guidBytes = hash[..16];
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var databaseName = $"vietride_payment_refund_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);
        try
        {
            await test(connectionString);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configured = Environment.GetEnvironmentVariable("PAYMENT_PERSISTENCE_TEST_CONNECTION");
        var template = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return new NpgsqlConnectionStringBuilder(
            template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase))
        {
            Database = databaseName,
        }.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FailingPaymentRefundedOutbox(IOutboxStore store) : IIntegrationEventOutbox
    {
        private readonly IntegrationEventOutbox _inner = new(store);

        public async Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken ct = default)
        {
            await _inner.EnqueueAsync(eventType, payloadJson, ct);
            if (eventType == "payment.payment.refunded")
            {
                throw new OutboxEnqueueTestException();
            }
        }
    }

    private sealed class FailingWalletCreditedOutbox(IOutboxStore store) : IIntegrationEventOutbox
    {
        private readonly IntegrationEventOutbox _inner = new(store);

        public async Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken ct = default)
        {
            await _inner.EnqueueAsync(eventType, payloadJson, ct);
            if (eventType == "payment.wallet.credited")
            {
                throw new OutboxEnqueueTestException();
            }
        }
    }

    private sealed class FailingRefundRevenueLedgerWriter(
        IOperatorLedgerEntryRepository ledger) : IRevenueLedgerWriter
    {
        private readonly RevenueLedgerWriter _inner = new(ledger);

        public Task RecordPaymentSucceededAsync(
            Guid sourceEventId,
            PaymentContextV1 context,
            CancellationToken cancellationToken)
            => _inner.RecordPaymentSucceededAsync(
                sourceEventId,
                context,
                cancellationToken);

        public async Task RecordRefundAsync(
            Guid sourceEventId,
            PaymentContextV1 context,
            Guid allocationReferenceId,
            long refundAmount,
            CancellationToken cancellationToken)
        {
            await _inner.RecordRefundAsync(
                sourceEventId,
                context,
                allocationReferenceId,
                refundAmount,
                cancellationToken);
            throw new OutboxEnqueueTestException();
        }

        public Task<bool> IsRefundRecordedAsync(
            Guid sourceEventId,
            Guid allocationReferenceId,
            CancellationToken cancellationToken)
            => _inner.IsRefundRecordedAsync(
                sourceEventId,
                allocationReferenceId,
                cancellationToken);
    }

    private sealed class OutboxEnqueueTestException : Exception
    {
    }

    private sealed class FrozenClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
