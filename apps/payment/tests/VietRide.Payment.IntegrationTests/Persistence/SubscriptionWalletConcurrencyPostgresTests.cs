using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using VietRide.Payment.Application;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Features.Internal.Payments.CreateSubscriptionPayment;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Infrastructure;
using VietRide.Payment.Infrastructure.DependencyInjection;
using VietRide.Shared.Application.DependencyInjection;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.DependencyInjection;

namespace VietRide.Payment.IntegrationTests.Persistence;

public sealed class SubscriptionWalletConcurrencyPostgresTests
{
    private const string ScratchPrefix = "vietride_subscription_wallet_race_";
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentWalletCreateForSameAttempt_DebitsAndCreditsExactlyOnce()
    {
        var databaseName = $"{ScratchPrefix}{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        var outbox = new CountingOutbox();
        await using var provider = CreateProvider(connectionString, outbox);
        await using var setupScope = provider.CreateAsyncScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        try
        {
            await setupDb.Database.MigrateAsync();
            var operatorId = Guid.NewGuid();
            var wallet = OperatorWallet.Create(operatorId);
            wallet.Credit(Money.FromRaw(1_000_000));
            setupDb.OperatorWallets.Add(wallet);
            setupDb.PlatformWallets.Add(PlatformWallet.Create());
            await setupDb.SaveChangesAsync();

            var attemptId = Guid.NewGuid();
            var subscriptionId = Guid.NewGuid();
            var planId = Guid.NewGuid();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = SendAsync(
                provider,
                gate.Task,
                CreateCommand(attemptId, subscriptionId, operatorId, planId, $"wallet-{Guid.NewGuid():N}"));
            var second = SendAsync(
                provider,
                gate.Task,
                CreateCommand(attemptId, subscriptionId, operatorId, planId, $"wallet-{Guid.NewGuid():N}"));
            gate.SetResult();

            var results = await Task.WhenAll(first, second);

            results[0].PaymentId.Should().Be(results[1].PaymentId);
            await using var assertScope = provider.CreateAsyncScope();
            var db = assertScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await db.Payments.CountAsync()).Should().Be(1);
            (await db.OperatorWalletTransactions.CountAsync()).Should().Be(1);
            (await db.PlatformWalletTransactions.CountAsync()).Should().Be(1);
            (await db.OperatorWallets.AsNoTracking().SingleAsync()).Balance.Amount.Should().Be(500_000);
            (await db.PlatformWallets.AsNoTracking().SingleAsync()).Balance.Amount.Should().Be(500_000);
            outbox.Count.Should().Be(1);
        }
        finally
        {
            var connectedDatabase = setupDb.Database.GetDbConnection().Database;
            if (!databaseName.StartsWith(ScratchPrefix, StringComparison.Ordinal)
                || !string.Equals(connectedDatabase, databaseName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Refusing to delete non-scratch database '{connectedDatabase}'.");
            }

            await setupDb.Database.EnsureDeletedAsync();
        }
    }

    private static CreateSubscriptionPaymentCommand CreateCommand(
        Guid attemptId,
        Guid subscriptionId,
        Guid operatorId,
        Guid planId,
        string idempotencyKey)
        => new(
            attemptId,
            subscriptionId,
            operatorId,
            planId,
            "MONTHLY",
            "WALLET",
            500_000,
            new SubscriptionPaymentContextV1(
                1,
                subscriptionId,
                planId,
                "Private Enterprise",
                "MONTHLY",
                Now,
                Now.AddMonths(1),
                new SubscriptionBuyerSnapshotV1(
                    "VietRide Bus",
                    "BRN-001",
                    "0312345678",
                    "billing@vietride.test",
                    "+84901234567",
                    null,
                    null,
                    "Ho Chi Minh City")),
            idempotencyKey,
            "203.0.113.10",
            Now.AddMinutes(15));

    private static async Task<CreateSubscriptionPaymentResult> SendAsync(
        ServiceProvider provider,
        Task gate,
        CreateSubscriptionPaymentCommand command)
    {
        await gate;
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    private static ServiceProvider CreateProvider(string connectionString, CountingOutbox outbox)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["InvoiceStorage:Provider"] = "E2E_LOCAL",
                ["InvoiceStorage:StableBaseUrl"] = "https://payment.test",
                ["OperatorWeb:InvoiceDetailBaseUrl"] = "https://operator.test/invoices",
                ["VNPAY_BASE_URL"] = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
                ["VNPAY_WEB_RETURN_URL"] = "https://operator.test/payment-return",
                ["VNPAY_MOBILE_SDK_RETURN_URL"] = "https://api.test/v1/payments/vnpay-mobile-sdk-return",
                ["VNPAY_IPN_URL"] = "https://api.test/v1/payments/vnpay-ipn",
                ["VNPAY_SDK_SCHEME"] = "vietride",
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
        services.RemoveAll<IVnPayClient>();
        services.AddSingleton<IVnPayClient, UnusedVnPayClient>();
        services.RemoveAll<IIntegrationEventOutbox>();
        services.AddSingleton<IIntegrationEventOutbox>(outbox);
        return services.BuildServiceProvider();
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_PAYMENT_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
            template = fallback;
        var expanded = template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
        return new NpgsqlConnectionStringBuilder(expanded) { Database = databaseName }.ConnectionString;
    }

    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class CountingOutbox : IIntegrationEventOutbox
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _count);
            return Task.CompletedTask;
        }
    }

    private sealed class UnusedVnPayClient : IVnPayClient
    {
        public string CreateTopUpRedirectUrl(
            Guid userId,
            Money amount,
            string reference,
            string clientIpAddress,
            DateTimeOffset now)
            => throw new NotSupportedException();

        public string CreateSubscriptionPaymentRedirectUrl(
            Guid upgradeAttemptId,
            Guid operatorId,
            Money amount,
            string txnRef,
            string clientIpAddress,
            DateTimeOffset now,
            DateTimeOffset dueAt,
            VietRide.Payment.Domain.Enums.VnPayReturnMode returnMode)
            => throw new NotSupportedException();

        public bool VerifySignature(IReadOnlyDictionary<string, string> parameters)
            => throw new NotSupportedException();

        public Task<bool> TryReserveIpnAsync(
            string vnPayTxnRef,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ReleaseIpnReservationAsync(
            string vnPayTxnRef,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
