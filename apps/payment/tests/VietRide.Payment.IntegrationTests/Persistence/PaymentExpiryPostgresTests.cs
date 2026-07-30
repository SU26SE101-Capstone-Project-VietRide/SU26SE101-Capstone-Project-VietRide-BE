using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VietRide.Payment.Application;
using VietRide.Payment.Application.Features.Payments.ExpirePayment;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure;
using VietRide.Payment.Infrastructure.DependencyInjection;
using VietRide.Shared.Application.DependencyInjection;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.DependencyInjection;
using VietRide.Shared.Persistence.Outbox;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.IntegrationTests.Persistence;

public sealed class PaymentExpiryPostgresTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PaymentExpiry_UsesPersistedOrLegacyDeadlineAndCommitsMatchingOutboxFacts()
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_PERSISTENCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var provider = CreateProvider(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var pastDue = CreatePendingPayment(PaymentReferenceType.BOOKING, Now.AddSeconds(-1));
        var boundaryDue = CreatePendingPayment(PaymentReferenceType.BOOKING, Now);
        var futureDueOldRow = CreatePendingPayment(PaymentReferenceType.BOOKING, Now.AddMinutes(1));
        var legacyBoundary = CreatePendingPayment(PaymentReferenceType.BOOKING, dueAt: null);
        var parcelFinalWindow = CreatePendingPayment(PaymentReferenceType.PARCEL_ADDITIONAL, Now.AddMinutes(15));
        var payments = new[] { pastDue, boundaryDue, futureDueOldRow, legacyBoundary, parcelFinalWindow };

        try
        {
            db.Payments.AddRange(payments);
            await db.SaveChangesAsync();
            await SetCreatedAtAsync(db, pastDue.Id, Now.AddMinutes(-1));
            await SetCreatedAtAsync(db, boundaryDue.Id, Now.AddMinutes(-1));
            await SetCreatedAtAsync(db, futureDueOldRow.Id, Now.AddMinutes(-30));
            await SetCreatedAtAsync(db, legacyBoundary.Id, Now.AddMinutes(-15));
            await SetCreatedAtAsync(db, parcelFinalWindow.Id, Now.AddMinutes(-15));
            db.ChangeTracker.Clear();

            var result = await mediator.Send(new ExpirePaymentCommand(Now));

            result.ExpiredCount.Should().Be(3);
            var statuses = await db.Payments
                .AsNoTracking()
                .Where(payment => payments.Select(item => item.Id).Contains(payment.Id))
                .ToDictionaryAsync(payment => payment.Id, payment => payment.Status);
            statuses[pastDue.Id].Should().Be(PaymentStatus.EXPIRED);
            statuses[boundaryDue.Id].Should().Be(PaymentStatus.EXPIRED);
            statuses[legacyBoundary.Id].Should().Be(PaymentStatus.EXPIRED);
            statuses[futureDueOldRow.Id].Should().Be(PaymentStatus.PENDING_REDIRECT);
            statuses[parcelFinalWindow.Id].Should().Be(PaymentStatus.PENDING_REDIRECT);

            var expiredIds = new[] { pastDue.Id, boundaryDue.Id, legacyBoundary.Id };
            var outboxPayloads = await db.OutboxEvents
                .AsNoTracking()
                .Where(item => item.EventType == "payment.payment.expired")
                .Select(item => item.Payload)
                .ToListAsync();
            foreach (var expiredId in expiredIds)
            {
                outboxPayloads.Count(payload => payload.Contains(expiredId.ToString(), StringComparison.OrdinalIgnoreCase))
                    .Should().Be(1);
            }

            var replay = await mediator.Send(new ExpirePaymentCommand(Now));

            replay.ExpiredCount.Should().Be(0);
            var replayOutboxPayloads = await db.OutboxEvents
                .AsNoTracking()
                .Where(item => item.EventType == "payment.payment.expired")
                .Select(item => item.Payload)
                .ToListAsync();
            foreach (var expiredId in expiredIds)
            {
                replayOutboxPayloads.Count(payload => payload.Contains(expiredId.ToString(), StringComparison.OrdinalIgnoreCase))
                    .Should().Be(1);
            }
        }
        finally
        {
            await DeleteFixturesAsync(db, payments.Select(payment => payment.Id).ToArray());
        }
    }

    [Fact]
    public async Task PaymentExpiry_WhenOutboxEnqueueFails_RollsBackCasTransition()
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_PERSISTENCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var provider = CreateProvider(connectionString, failAfterOutboxEnqueue: true);
        var payment = CreatePendingPayment(PaymentReferenceType.BOOKING, Now);

        try
        {
            await using (var setupScope = provider.CreateAsyncScope())
            {
                var setupDb = setupScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                setupDb.Payments.Add(payment);
                await setupDb.SaveChangesAsync();
                await SetCreatedAtAsync(setupDb, payment.Id, Now.AddMinutes(-1));
            }

            await using (var commandScope = provider.CreateAsyncScope())
            {
                var mediator = commandScope.ServiceProvider.GetRequiredService<IMediator>();
                var action = () => mediator.Send(new ExpirePaymentCommand(Now));

                await action.Should().ThrowAsync<OutboxEnqueueTestException>();
            }

            await using var assertScope = provider.CreateAsyncScope();
            var assertDb = assertScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var reloaded = await assertDb.Payments.AsNoTracking().SingleAsync(item => item.Id == payment.Id);
            reloaded.Status.Should().Be(PaymentStatus.PENDING_REDIRECT);
            reloaded.ExpiredAt.Should().BeNull();
            var outboxPayloads = await assertDb.OutboxEvents
                .AsNoTracking()
                .Where(item => item.EventType == "payment.payment.expired")
                .Select(item => item.Payload)
                .ToListAsync();
            outboxPayloads.Should().NotContain(
                payload => payload.Contains(payment.Id.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await using var cleanupScope = provider.CreateAsyncScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            await DeleteFixturesAsync(cleanupDb, [payment.Id]);
        }
    }

    private static ServiceProvider CreateProvider(
        string connectionString,
        bool failAfterOutboxEnqueue = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new FrozenClock(Now));
        services.AddVietRideDbContext<PaymentDbContext>(
            configuration,
            configureDataSource: PaymentDbContext.ConfigurePostgresTypes);
        services.AddVietRideMediatRBehaviors(
            handlerAssemblies: [typeof(ApplicationAssemblyMarker).Assembly]);
        services.AddInfrastructure(configuration, registerConsumers: false);
        if (failAfterOutboxEnqueue)
        {
            services.RemoveAll<IIntegrationEventOutbox>();
            services.AddScoped<IIntegrationEventOutbox, FailingAfterEnqueueOutbox>();
        }

        return services.BuildServiceProvider();
    }

    private static PaymentEntity CreatePendingPayment(
        PaymentReferenceType referenceType,
        DateTimeOffset? dueAt)
        => PaymentEntity.CreatePendingRedirectVnPay(
            referenceType,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.FromRaw(250_000),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("N"),
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
            dueAt);

    private static Task<int> SetCreatedAtAsync(
        PaymentDbContext db,
        Guid paymentId,
        DateTimeOffset createdAt)
        => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE vietride_payment.payments SET created_at = {createdAt}, updated_at = {createdAt} WHERE id = {paymentId}");

    private static async Task DeleteFixturesAsync(PaymentDbContext db, Guid[] paymentIds)
    {
        var paymentIdTexts = paymentIds.Select(id => id.ToString()).ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM vietride_payment.outbox_events WHERE payload ->> 'paymentId' = ANY({paymentIdTexts})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM vietride_payment.payments WHERE id = ANY({paymentIds})");
    }

    private sealed class FailingAfterEnqueueOutbox : IIntegrationEventOutbox
    {
        private readonly IntegrationEventOutbox _inner;

        public FailingAfterEnqueueOutbox(IOutboxStore store)
        {
            _inner = new IntegrationEventOutbox(store);
        }

        public async Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken ct = default)
        {
            await _inner.EnqueueAsync(eventType, payloadJson, ct);
            throw new OutboxEnqueueTestException();
        }
    }

    private sealed class OutboxEnqueueTestException : Exception;

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
