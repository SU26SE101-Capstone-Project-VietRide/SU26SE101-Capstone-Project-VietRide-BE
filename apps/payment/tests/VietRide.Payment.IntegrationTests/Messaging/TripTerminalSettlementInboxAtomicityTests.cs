using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Features.Settlements.HandleTripTerminal;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure;
using VietRide.Payment.Infrastructure.DependencyInjection;
using VietRide.Shared.Application.Inbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence.DependencyInjection;

namespace VietRide.Payment.IntegrationTests.Messaging;

public sealed class TripTerminalSettlementInboxAtomicityTests
{
    private const string TerminalConsumer = "payment.trip-terminal-settlement";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-02T02:00:00Z");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedAndDisrupted_CreateSettlementAndBothMarkersInOneInboxCommit(bool disrupted)
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var evt = CreateEvent(disrupted);
            await using var provider = CreateProvider(connectionString);
            await MigrateAndSeedLedgerAsync(provider, evt.OperatorId, evt.TripId);

            var result = await DeliverAsync(provider, evt, disrupted);

            result.Should().Be(IntegrationEventInboxResult.Processed);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var settlement = await db.OperatorTripSettlements.AsNoTracking().SingleAsync();
            settlement.OperatorId.Should().Be(evt.OperatorId);
            settlement.TripId.Should().Be(evt.TripId);
            settlement.Status.Should().Be(OperatorTripSettlementStatus.PENDING_HOLD);
            settlement.NetAmount.Should().Be(500_000);
            (await db.ProcessedIntegrationEvents.AsNoTracking()
                    .Where(row => row.EventId == evt.EventId)
                    .Select(row => row.Consumer)
                    .ToListAsync())
                .Should().BeEquivalentTo(TerminalConsumer, QueueName(disrupted));
        });
    }

    [Fact]
    public async Task FailureAfterTerminalHandler_RollsBackSettlementAndBothMarkers()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var evt = CreateEvent(disrupted: false);
            await using var provider = CreateProvider(connectionString);
            await MigrateAndSeedLedgerAsync(provider, evt.OperatorId, evt.TripId);

            var action = () => DeliverAsync(
                provider,
                evt,
                disrupted: false,
                failAfterHandler: true);

            await action.Should().ThrowAsync<TerminalInboxFailureException>();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await db.OperatorTripSettlements.AsNoTracking().CountAsync()).Should().Be(0);
            (await db.ProcessedIntegrationEvents.AsNoTracking()
                .CountAsync(row => row.EventId == evt.EventId)).Should().Be(0);
        });
    }

    [Fact]
    public async Task Replay_IsInboxDuplicateAndCreatesNoAdditionalSettlementOrMarker()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var evt = CreateEvent(disrupted: true);
            await using var provider = CreateProvider(connectionString);
            await MigrateAndSeedLedgerAsync(provider, evt.OperatorId, evt.TripId);

            (await DeliverAsync(provider, evt, disrupted: true))
                .Should().Be(IntegrationEventInboxResult.Processed);
            (await DeliverAsync(provider, evt, disrupted: true))
                .Should().Be(IntegrationEventInboxResult.Duplicate);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await db.OperatorTripSettlements.AsNoTracking().CountAsync()).Should().Be(1);
            (await db.ProcessedIntegrationEvents.AsNoTracking()
                .CountAsync(row => row.EventId == evt.EventId)).Should().Be(2);
        });
    }

    [Fact]
    public async Task DirectServiceInvocation_OwnsAndCommitsItsTransaction()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var evt = CreateEvent(disrupted: false);
            await using var provider = CreateProvider(connectionString);
            await MigrateAndSeedLedgerAsync(provider, evt.OperatorId, evt.TripId);

            await using (var scope = provider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<TripTerminalSettlementService>();
                await service.HandleAsync(
                    evt.EventId,
                    evt.OperatorId,
                    evt.TripId,
                    evt.TerminalAt,
                    CancellationToken.None);
            }

            await using var assertScope = provider.CreateAsyncScope();
            var db = assertScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            (await db.OperatorTripSettlements.AsNoTracking().CountAsync()).Should().Be(1);
            (await db.ProcessedIntegrationEvents.AsNoTracking()
                .CountAsync(row => row.EventId == evt.EventId && row.Consumer == TerminalConsumer))
                .Should().Be(1);
        });
    }

    private static async Task<IntegrationEventInboxResult> DeliverAsync(
        ServiceProvider provider,
        TerminalEventData evt,
        bool disrupted,
        bool failAfterHandler = false)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var service = services.GetRequiredService<TripTerminalSettlementService>();
        var payload = disrupted
            ? JsonSerializer.Serialize(new TripDisruptedConsumerEvent(
                evt.EventId,
                evt.OccurredAt,
                evt.TripId,
                evt.OperatorId,
                evt.TerminalAt,
                false,
                "VEHICLE_BREAKDOWN"), JsonOptions)
            : JsonSerializer.Serialize(new TripCompletedConsumerEvent(
                evt.EventId,
                evt.OccurredAt,
                evt.TripId,
                evt.OperatorId,
                evt.TerminalAt,
                false), JsonOptions);
        var inbox = services.GetRequiredService<IIntegrationEventInbox>();
        return await inbox.ExecuteAsync(
            QueueName(disrupted),
            evt.EventId,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant(),
            async ct =>
            {
                if (disrupted)
                {
                    var handler = new TripDisruptedSettlementEventHandler(service);
                    await handler.HandleAsync(
                        new TripDisruptedConsumerEvent(
                            evt.EventId,
                            evt.OccurredAt,
                            evt.TripId,
                            evt.OperatorId,
                            evt.TerminalAt,
                            false,
                            "VEHICLE_BREAKDOWN"),
                        ct);
                }
                else
                {
                    var handler = new TripCompletedSettlementEventHandler(service);
                    await handler.HandleAsync(
                        new TripCompletedConsumerEvent(
                            evt.EventId,
                            evt.OccurredAt,
                            evt.TripId,
                            evt.OperatorId,
                            evt.TerminalAt,
                            false),
                        ct);
                }

                if (failAfterHandler)
                    throw new TerminalInboxFailureException();
            },
            CancellationToken.None);
    }

    private static async Task MigrateAndSeedLedgerAsync(
        ServiceProvider provider,
        Guid operatorId,
        Guid tripId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await db.Database.MigrateAsync();
        db.OperatorLedgerEntries.Add(OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            OperatorLedgerEntryType.BOOKING_REVENUE,
            500_000,
            OperatorLedgerReferenceType.BOOKING,
            Guid.NewGuid(),
            Guid.NewGuid()));
        await db.SaveChangesAsync();
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
        return services.BuildServiceProvider();
    }

    private static TerminalEventData CreateEvent(bool disrupted)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Now.UtcDateTime,
            Now.AddHours(disrupted ? -2 : -1));

    private static string QueueName(bool disrupted)
        => disrupted
            ? "payment.trip-disrupted-settlement"
            : "payment.trip-completed-settlement";

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var databaseName = $"vietride_payment_terminal_{Guid.NewGuid():N}";
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

    private sealed record TerminalEventData(
        Guid EventId,
        Guid OperatorId,
        Guid TripId,
        DateTime OccurredAt,
        DateTimeOffset TerminalAt);

    private sealed class FrozenClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class TerminalInboxFailureException : Exception;
}
