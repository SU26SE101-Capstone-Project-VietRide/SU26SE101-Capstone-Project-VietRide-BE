using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using VietRide.Payment.Application.Features.Invoices;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure;
using VietRide.Payment.Infrastructure.DependencyInjection;
using VietRide.Shared.Application.Inbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.DependencyInjection;

namespace VietRide.Payment.IntegrationTests.Messaging;

public sealed class SubscriptionInvoiceInboxAtomicityTests
{
    private const string QueueConsumer = "payment.subscription-invoice";
    private const string HandlerConsumer = "payment.subscription-invoice-handler";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-02T02:00:00Z");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Delivery_CreatesDraftInvoiceAndBothMarkersInOneInboxCommit()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var provider = CreateProvider(connectionString);
            await MigrateAsync(provider);
            var integrationEvent = await SeedPaymentAndCreateEventAsync(provider);

            var result = await DeliverAsync(provider, integrationEvent);

            result.Should().Be(IntegrationEventInboxResult.Processed);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var invoice = await db.Invoices.AsNoTracking().SingleAsync();
            invoice.PaymentId.Should().Be(integrationEvent.PaymentId);
            invoice.Status.Should().Be(InvoiceStatus.DRAFT);
            invoice.PdfGenerationStatus.Should().Be(InvoicePdfGenerationStatus.PENDING);
            invoice.InvoiceNumber.Should().Be("VR-INV-202608-000001");
            (await db.ProcessedIntegrationEvents.AsNoTracking()
                    .Where(row => row.EventId == integrationEvent.EventId)
                    .Select(row => row.Consumer)
                    .ToListAsync())
                .Should().BeEquivalentTo(HandlerConsumer, QueueConsumer);
            provider.GetRequiredService<RecordingInvoiceJobScheduler>().InvoiceIds
                .Should().ContainSingle().Which.Should().Be(invoice.Id);
        });
    }

    [Fact]
    public async Task FailureAfterHandler_RollsBackInvoiceCounterAndBothMarkers()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var provider = CreateProvider(connectionString);
            await MigrateAsync(provider);
            var integrationEvent = await SeedPaymentAndCreateEventAsync(provider);

            var action = () => DeliverAsync(provider, integrationEvent, failAfterHandler: true);

            await action.Should().ThrowAsync<InvoiceInboxFailureException>();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await db.Invoices.AsNoTracking().CountAsync()).Should().Be(0);
            (await db.InvoiceNumberCounters.AsNoTracking().CountAsync()).Should().Be(0);
            (await db.ProcessedIntegrationEvents.AsNoTracking()
                .CountAsync(row => row.EventId == integrationEvent.EventId)).Should().Be(0);
        });
    }

    [Fact]
    public async Task Replay_IsInboxDuplicateAndDoesNotCreateAnotherInvoiceMarkerOrJob()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var provider = CreateProvider(connectionString);
            await MigrateAsync(provider);
            var integrationEvent = await SeedPaymentAndCreateEventAsync(provider);

            (await DeliverAsync(provider, integrationEvent))
                .Should().Be(IntegrationEventInboxResult.Processed);
            (await DeliverAsync(provider, integrationEvent))
                .Should().Be(IntegrationEventInboxResult.Duplicate);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await db.Invoices.AsNoTracking().CountAsync()).Should().Be(1);
            (await db.InvoiceNumberCounters.AsNoTracking().SingleAsync()).LastValue.Should().Be(1);
            (await db.ProcessedIntegrationEvents.AsNoTracking()
                .CountAsync(row => row.EventId == integrationEvent.EventId)).Should().Be(2);
            provider.GetRequiredService<RecordingInvoiceJobScheduler>().InvoiceIds.Should().ContainSingle();
        });
    }

    [Fact]
    public async Task DirectHandlerInvocation_OwnsAndCommitsItsTransaction()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var provider = CreateProvider(connectionString);
            await MigrateAsync(provider);
            var integrationEvent = await SeedPaymentAndCreateEventAsync(provider);

            await using (var scope = provider.CreateAsyncScope())
            {
                var handler = scope.ServiceProvider
                    .GetRequiredService<SubscriptionPaymentSucceededInvoiceHandler>();
                await handler.HandleAsync(integrationEvent, CancellationToken.None);
            }

            await using var assertScope = provider.CreateAsyncScope();
            var db = assertScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var invoice = await db.Invoices.AsNoTracking().SingleAsync();
            (await db.ProcessedIntegrationEvents.AsNoTracking()
                .CountAsync(row => row.EventId == integrationEvent.EventId
                    && row.Consumer == HandlerConsumer)).Should().Be(1);
            provider.GetRequiredService<RecordingInvoiceJobScheduler>().InvoiceIds
                .Should().ContainSingle().Which.Should().Be(invoice.Id);
        });
    }

    private static async Task<IntegrationEventInboxResult> DeliverAsync(
        ServiceProvider provider,
        SubscriptionPaymentSucceededInvoiceEvent integrationEvent,
        bool failAfterHandler = false)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var handler = services.GetRequiredService<SubscriptionPaymentSucceededInvoiceHandler>();
        var payload = JsonSerializer.Serialize(integrationEvent, JsonOptions);
        var inbox = services.GetRequiredService<IIntegrationEventInbox>();
        return await inbox.ExecuteAsync(
            QueueConsumer,
            integrationEvent.EventId,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
            async ct =>
            {
                await handler.HandleAsync(integrationEvent, ct);
                if (failAfterHandler)
                    throw new InvoiceInboxFailureException();
            },
            CancellationToken.None);
    }

    private static async Task MigrateAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await db.Database.MigrateAsync();
    }

    private static ServiceProvider CreateProvider(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["REDIS_URL"] = "localhost:6379",
                ["RabbitMq:ExchangeName"] = "vietride.events",
                ["InvoiceStorage:Provider"] = "E2E_LOCAL",
                ["VNPAY_BASE_URL"] = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
                ["VNPAY_RETURN_URL"] = "https://example.test/vnpay-return",
                ["VNPAY_IPN_URL"] = "https://example.test/v1/payments/vnpay-ipn",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new FrozenClock());
        services.AddVietRideDbContext<PaymentDbContext>(
            configuration,
            configureDataSource: PaymentDbContext.ConfigurePostgresTypes,
            configureDbContext: options => options.ConfigureWarnings(
                warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddInfrastructure(configuration, registerConsumers: false);
        services.RemoveAll<IInvoiceJobScheduler>();
        services.AddSingleton<RecordingInvoiceJobScheduler>();
        services.AddSingleton<IInvoiceJobScheduler>(provider =>
            provider.GetRequiredService<RecordingInvoiceJobScheduler>());
        services.AddScoped<SubscriptionPaymentSucceededInvoiceHandler>();
        return services.BuildServiceProvider();
    }

    private static async Task<SubscriptionPaymentSucceededInvoiceEvent> SeedPaymentAndCreateEventAsync(
        ServiceProvider provider)
    {
        var upgradeAttemptId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var payment = VietRide.Payment.Domain.Entities.Payment.CreatePendingRedirectVnPaySubscription(
            upgradeAttemptId,
            operatorId,
            Money.FromRaw(500_000),
            $"SUB-{Guid.NewGuid():N}",
            $"invoice-{Guid.NewGuid():N}",
            "https://example.test/pay",
            Now.AddMinutes(15));
        payment.MarkSucceeded("00", Now);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
        }

        return new SubscriptionPaymentSucceededInvoiceEvent(
            Guid.NewGuid(),
            Now.UtcDateTime,
            payment.Id,
            upgradeAttemptId,
            operatorId,
            Guid.NewGuid(),
            500_000,
            "WALLET",
            "Business",
            "MONTHLY",
            Now,
            Now.AddMonths(1),
            new SubscriptionBuyerSnapshotV1(
                "Nha xe Viet",
                "BR-001",
                "0312345678",
                "billing@example.test",
                "0900000000",
                "1 Nguyen Hue",
                null,
                "TP.HCM"));
    }

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var databaseName = $"vietride_payment_invoice_inbox_{Guid.NewGuid():N}";
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
        var configured = Environment.GetEnvironmentVariable("VIETRIDE_PAYMENT_TEST_CONNECTION_STRING");
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

    private sealed class RecordingInvoiceJobScheduler : IInvoiceJobScheduler
    {
        public List<Guid> InvoiceIds { get; } = [];

        public void EnqueuePdfGeneration(Guid invoiceId) => InvoiceIds.Add(invoiceId);
    }

    private sealed class FrozenClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class InvoiceInboxFailureException : Exception;
}
