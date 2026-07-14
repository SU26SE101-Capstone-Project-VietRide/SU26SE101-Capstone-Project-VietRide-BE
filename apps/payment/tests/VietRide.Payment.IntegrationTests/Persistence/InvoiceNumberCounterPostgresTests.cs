using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Infrastructure;
using VietRide.Payment.Infrastructure.DependencyInjection;
using VietRide.Payment.Infrastructure.Jobs;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;
using PaymentMethod = VietRide.Payment.Domain.Enums.PaymentMethod;
using PaymentReferenceType = VietRide.Payment.Domain.Enums.PaymentReferenceType;

namespace VietRide.Payment.IntegrationTests.Persistence;

public sealed class InvoiceNumberCounterPostgresTests
{
    [Fact]
    public async Task PaymentContext_RoundTripsJsonbWithoutRoundingMoney()
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_PERSISTENCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var provider = CreateProvider(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var payment = PaymentEntity.CreatePendingRedirect(
            PaymentReferenceType.BOOKING,
            Guid.NewGuid(),
            Money.FromRaw(125_001),
            PaymentMethod.VNPAY);
        payment.AttachContext("{\"version\":1,\"allocations\":[{\"grossAmount\":125001}]}");
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.Payments.SingleAsync(x => x.Id == payment.Id);
        using var contextDocument = JsonDocument.Parse(loaded.Context);
        contextDocument.RootElement.GetProperty("version").GetInt32().Should().Be(1);
        contextDocument.RootElement.GetProperty("allocations")[0]
            .GetProperty("grossAmount").GetInt64().Should().Be(125_001);
        loaded.Amount.Amount.Should().Be(125_001);
        loaded.ContextReconciliationRequired.Should().BeFalse();

        db.Payments.Remove(loaded);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Counter_IsAtomicUnderConcurrencyAndResetsByPeriodKey()
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_PERSISTENCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var provider = CreateProvider(connectionString);
        await DeletePeriodsAsync(provider, "209901", "209902");

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => AllocateAsync(provider, "209901"));
        var allocated = await Task.WhenAll(tasks);

        allocated.Order().Should().Equal(Enumerable.Range(1, 10).Select(x => (long)x));
        (await AllocateAsync(provider, "209902")).Should().Be(1);
    }

    [Fact]
    public async Task RevenueBackfill_SelectsPaymentCreatedBeforeCutoffWhenCallbackSucceededAfterCutoff()
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_PERSISTENCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddHours(-1);
        var legacy = CreateSucceededPayment(now);
        var current = CreateSucceededPayment(now);

        await using var provider = CreateProvider(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        db.Payments.AddRange(legacy, current);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE vietride_payment.payments SET created_at={now.AddHours(-2)} WHERE id={legacy.Id}");
        db.ChangeTracker.Clear();

        var writer = new CapturingRevenueLedgerWriter();
        var job = new Day38RevenueLedgerBackfillJob(
            db,
            scope.ServiceProvider.GetRequiredService<IOperatorLedgerEntryRepository>(),
            writer,
            Options.Create(new Day38RevenueLedgerBackfillOptions
            {
                Enabled = true,
                LegacyCutoffUtc = cutoff,
                MaxBatchSize = 10,
            }),
            NullLogger<Day38RevenueLedgerBackfillJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        writer.SourceEventIds.Should().Equal(legacy.Id);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM vietride_payment.payments WHERE id IN ({legacy.Id}, {current.Id})");
    }

    private static PaymentEntity CreateSucceededPayment(DateTimeOffset succeededAt)
    {
        var referenceId = Guid.NewGuid();
        var payment = PaymentEntity.CreatePendingRedirect(
            PaymentReferenceType.BOOKING,
            referenceId,
            Money.FromRaw(125_000),
            PaymentMethod.VNPAY,
            userId: Guid.NewGuid());
        payment.AttachContext(PaymentContextCodec.ValidateAndSerialize(
            new PaymentContextV1(1,
            [
                new PaymentAllocationV1(
                    referenceId,
                    "BOOKING",
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    125_000,
                    0,
                    0),
            ]),
            "BOOKING",
            referenceId,
            125_000));
        payment.MarkSucceeded("00", succeededAt);
        return payment;
    }

    private static ServiceProvider CreateProvider(string connectionString)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        PaymentDbContext.ConfigurePostgresTypes(dataSourceBuilder);
        var dataSource = dataSourceBuilder.Build();

        var services = new ServiceCollection();
        services.AddSingleton<IClock, SystemClock>();
        services.AddDbContext<PaymentDbContext>(options => options.UseNpgsql(dataSource));
        services.AddInfrastructure(new ConfigurationBuilder().Build(), registerConsumers: false);
        return services.BuildServiceProvider();
    }

    private static async Task<long> AllocateAsync(IServiceProvider provider, string periodKey)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var counter = scope.ServiceProvider.GetRequiredService<IInvoiceNumberCounterRepository>();
        var value = await counter.NextAsync(periodKey, CancellationToken.None);
        await transaction.CommitAsync();
        return value;
    }

    private static async Task DeletePeriodsAsync(IServiceProvider provider, params string[] periodKeys)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM vietride_payment.invoice_number_counters WHERE period_key = ANY({periodKeys})");
    }

    private sealed class CapturingRevenueLedgerWriter : IRevenueLedgerWriter
    {
        public List<Guid> SourceEventIds { get; } = [];

        public Task RecordPaymentSucceededAsync(
            Guid sourceEventId,
            PaymentContextV1 context,
            CancellationToken cancellationToken)
        {
            SourceEventIds.Add(sourceEventId);
            return Task.CompletedTask;
        }
    }
}
