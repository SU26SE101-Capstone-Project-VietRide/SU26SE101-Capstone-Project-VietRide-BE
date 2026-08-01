using System.Globalization;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VietRide.Payment.Application;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Features.Payments.ConfirmBookingPayment;
using VietRide.Payment.Application.Features.Payments.ExpirePayment;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Application.Services;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure;
using VietRide.Payment.Infrastructure.DependencyInjection;
using VietRide.Shared.Application.DependencyInjection;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.DependencyInjection;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.IntegrationTests.Persistence;

public sealed class ExpiredPaymentRacePostgresTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 3, 0, 0, TimeSpan.Zero);
    private const long Amount = 250_000;

    [Fact]
    public async Task ExpiredPaymentRace_WhenExpiryWins_OnTimeCaptureIsRecordedExactlyOnce()
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_PERSISTENCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var provider = CreateProvider(connectionString);
        var fixture = CreatePayment(PaymentReferenceType.BOOKING, Now, "expiry-wins");
        await InsertFixtureAsync(provider, fixture.Payment);

        try
        {
            await using (var expiryScope = provider.CreateAsyncScope())
            {
                var db = expiryScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                var payments = expiryScope.ServiceProvider.GetRequiredService<IPaymentRepository>();
                await using var transaction = await db.Database.BeginTransactionAsync();
                var expired = await payments.ExpirePendingRedirectDueAsync(
                    Now.AddMinutes(-15),
                    Now,
                    CancellationToken.None);
                expired.Should().ContainSingle(payment => payment.Id == fixture.Payment.Id);

                var callbackTask = SendIpnAsync(
                    provider,
                    fixture.Payment.VnPayTxnRef!,
                    Now.AddSeconds(-1));
                await transaction.CommitAsync();

                var callback = await callbackTask;
                callback.RspCode.Should().Be("00");
            }

            await AssertExactlyOneCaptureAsync(provider, fixture, expectExpiredAt: true);
        }
        finally
        {
            await DeleteFixtureAsync(provider, fixture);
        }
    }

    [Fact]
    public async Task ExpiredPaymentRace_WhenIpnWins_ExpiryCasDoesNotOverwriteCapture()
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_PERSISTENCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var gate = new RevenueWriterGate();
        await using var provider = CreateProvider(connectionString, gate);
        var fixture = CreatePayment(PaymentReferenceType.BOOKING, Now, "ipn-wins");
        await InsertFixtureAsync(provider, fixture.Payment);

        try
        {
            var callbackTask = SendIpnAsync(
                provider,
                fixture.Payment.VnPayTxnRef!,
                Now.AddSeconds(-1));
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var expiryTask = SendExpiryAsync(provider);
            gate.Release.TrySetResult();

            var callback = await callbackTask;
            var expiry = await expiryTask;
            callback.RspCode.Should().Be("00");
            expiry.ExpiredCount.Should().Be(0);
            await AssertExactlyOneCaptureAsync(provider, fixture, expectExpiredAt: false);
        }
        finally
        {
            gate.Release.TrySetResult();
            await DeleteFixtureAsync(provider, fixture);
        }
    }

    [Fact]
    public async Task ExpiredPaymentRace_WhenTwoCallbacksRunConcurrently_CaptureFactsAreNotDuplicated()
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_PERSISTENCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var provider = CreateProvider(connectionString);
        var fixture = CreatePayment(PaymentReferenceType.BOOKING, Now.AddMinutes(1), "concurrent-ipn");
        await InsertFixtureAsync(provider, fixture.Payment);

        try
        {
            var callbacks = await Task.WhenAll(
                SendIpnAsync(provider, fixture.Payment.VnPayTxnRef!, Now),
                SendIpnAsync(provider, fixture.Payment.VnPayTxnRef!, Now));

            callbacks.Select(result => result.RspCode).Should().BeEquivalentTo("00", "02");
            await AssertExactlyOneCaptureAsync(provider, fixture, expectExpiredAt: false);

            var replay = await SendIpnAsync(provider, fixture.Payment.VnPayTxnRef!, Now);
            replay.RspCode.Should().Be("02");
            await AssertExactlyOneCaptureAsync(provider, fixture, expectExpiredAt: false);
        }
        finally
        {
            await DeleteFixtureAsync(provider, fixture);
        }
    }

    [Fact]
    public async Task ExpiredPaymentRace_WhenParcelCaptureIsAtDeadline_RecordsSuccessAndRefundFactsOnce()
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_PERSISTENCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var provider = CreateProvider(connectionString);
        var fixture = CreatePayment(PaymentReferenceType.PARCEL_ADDITIONAL, Now, "late-parcel");
        await InsertFixtureAsync(provider, fixture.Payment, markExpired: true);

        try
        {
            var callback = await SendIpnAsync(provider, fixture.Payment.VnPayTxnRef!, Now);
            callback.RspCode.Should().Be("00");
            await AssertExactlyOneCaptureAsync(provider, fixture, expectExpiredAt: true);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var outbox = await db.OutboxEvents
                .AsNoTracking()
                .Where(item => item.EventType == "payment.payment.succeeded"
                    || item.EventType == "parcel.refund.initiated")
                .ToListAsync();
            outbox.Should().ContainSingle(item =>
                item.EventType == "payment.payment.succeeded"
                && item.Payload.Contains(fixture.Payment.Id.ToString(), StringComparison.OrdinalIgnoreCase));
            outbox.Should().ContainSingle(item =>
                item.EventType == "parcel.refund.initiated"
                && item.Payload.Contains(fixture.Payment.Id.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await DeleteFixtureAsync(provider, fixture);
        }
    }

    private static ServiceProvider CreateProvider(
        string connectionString,
        RevenueWriterGate? gate = null)
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
        services.RemoveAll<IVnPayClient>();
        services.AddSingleton<IVnPayClient, AllowAllVnPayClient>();
        if (gate is not null)
        {
            services.AddSingleton(gate);
            services.RemoveAll<IRevenueLedgerWriter>();
            services.AddScoped<IRevenueLedgerWriter, BlockingRevenueLedgerWriter>();
        }

        return services.BuildServiceProvider();
    }

    private static PaymentFixture CreatePayment(
        PaymentReferenceType referenceType,
        DateTimeOffset dueAt,
        string txnRefPrefix)
    {
        var referenceId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var payment = PaymentEntity.CreatePendingRedirectVnPay(
            referenceType,
            referenceId,
            Guid.NewGuid(),
            Money.FromRaw(Amount),
            $"{txnRefPrefix}-{Guid.NewGuid():N}",
            Guid.NewGuid().ToString("N"),
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
            dueAt);
        var allocationType = referenceType == PaymentReferenceType.PARCEL_ADDITIONAL
            ? "PARCEL_ADDITIONAL"
            : "BOOKING";
        payment.AttachContext(PaymentContextCodec.ValidateAndSerialize(
            new PaymentContextV1(1,
            [
                new PaymentAllocationV1(
                    referenceId,
                    allocationType,
                    operatorId,
                    tripId,
                    Amount,
                    0,
                    0),
            ]),
            referenceType.ToString(),
            referenceId,
            Amount));
        return new PaymentFixture(payment, referenceId);
    }

    private static async Task InsertFixtureAsync(
        ServiceProvider provider,
        PaymentEntity payment,
        bool markExpired = false)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        if (!await db.PlatformWallets.AnyAsync())
        {
            db.PlatformWallets.Add(PlatformWallet.Create());
        }

        if (markExpired)
        {
            payment.MarkExpired(Now);
        }

        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE vietride_payment.payments SET created_at = {Now.AddMinutes(-1)}, updated_at = {Now.AddMinutes(-1)} WHERE id = {payment.Id}");
    }

    private static async Task<ConfirmBookingPaymentResult> SendIpnAsync(
        ServiceProvider provider,
        string txnRef,
        DateTimeOffset paidAt)
    {
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(new ConfirmBookingPaymentCommand(
            new Dictionary<string, string>
            {
                ["vnp_TxnRef"] = txnRef,
                ["vnp_ResponseCode"] = "00",
                ["vnp_Amount"] = checked(Amount * 100).ToString(CultureInfo.InvariantCulture),
                ["vnp_TransactionStatus"] = "00",
                ["vnp_PayDate"] = paidAt
                    .ToOffset(TimeSpan.FromHours(7))
                    .ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
                ["vnp_SecureHash"] = "valid",
            }));
    }

    private static async Task<ExpirePaymentResult> SendExpiryAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new ExpirePaymentCommand(Now));
    }

    private static async Task AssertExactlyOneCaptureAsync(
        ServiceProvider provider,
        PaymentFixture fixture,
        bool expectExpiredAt)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var payment = await db.Payments.AsNoTracking().SingleAsync(item => item.Id == fixture.Payment.Id);
        payment.Status.Should().Be(PaymentStatus.SUCCEEDED);
        payment.SucceededAt.Should().NotBeNull();
        if (expectExpiredAt)
            payment.ExpiredAt.Should().Be(Now);
        else
            payment.ExpiredAt.Should().BeNull();

        var platformReference = fixture.Payment.ReferenceType == PaymentReferenceType.PARCEL_ADDITIONAL
            ? PlatformWalletTransactionRef.PARCEL_ADDITIONAL_PAYMENT_HOLD
            : PlatformWalletTransactionRef.BOOKING_PAYMENT_HOLD;
        (await db.PlatformWalletTransactions.CountAsync(item =>
            item.ReferenceType == platformReference
            && item.ReferenceId == fixture.ReferenceId)).Should().Be(1);
        (await db.OperatorLedgerEntries.CountAsync(item => item.ReferenceId == fixture.ReferenceId))
            .Should().Be(1);
        var outbox = await db.OutboxEvents
            .AsNoTracking()
            .Where(item => item.EventType == "payment.payment.succeeded")
            .Select(item => item.Payload)
            .ToListAsync();
        outbox.Count(payload => payload.Contains(
            fixture.Payment.Id.ToString(),
            StringComparison.OrdinalIgnoreCase)).Should().Be(1);
    }

    private static async Task DeleteFixtureAsync(
        ServiceProvider provider,
        PaymentFixture fixture)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var paymentId = fixture.Payment.Id.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM vietride_payment.outbox_events WHERE payload::text LIKE {"%" + paymentId + "%"}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM vietride_payment.operator_ledger_entries WHERE reference_id = {fixture.ReferenceId}");
        var removedCredits = await db.PlatformWalletTransactions
            .Where(item => item.ReferenceId == fixture.ReferenceId)
            .ExecuteDeleteAsync();
        if (removedCredits > 0)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE vietride_payment.platform_wallets SET balance = balance - {Amount}, row_version = row_version + 1, updated_at = now()");
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM vietride_payment.payments WHERE id = {fixture.Payment.Id}");
    }

    private sealed record PaymentFixture(PaymentEntity Payment, Guid ReferenceId);

    private sealed class AllowAllVnPayClient : IVnPayClient
    {
        public string CreateTopUpRedirectUrl(
            Guid userId,
            Money amount,
            string vnPayTxnRef,
            string clientIpAddress,
            DateTimeOffset createdAt)
            => throw new NotSupportedException();

        public bool VerifySignature(IReadOnlyDictionary<string, string> parameters) => true;

        public bool IsExpectedMerchant(IReadOnlyDictionary<string, string> parameters) => true;

        public Task<bool> TryReserveIpnAsync(string vnPayTxnRef, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class RevenueWriterGate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BlockingRevenueLedgerWriter : IRevenueLedgerWriter
    {
        private readonly RevenueLedgerWriter _inner;
        private readonly RevenueWriterGate _gate;

        public BlockingRevenueLedgerWriter(
            IOperatorLedgerEntryRepository ledger,
            RevenueWriterGate gate)
        {
            _inner = new RevenueLedgerWriter(ledger);
            _gate = gate;
        }

        public async Task RecordPaymentSucceededAsync(
            Guid sourceEventId,
            PaymentContextV1 context,
            CancellationToken cancellationToken)
        {
            _gate.Entered.TrySetResult();
            await _gate.Release.Task.WaitAsync(cancellationToken);
            await _inner.RecordPaymentSucceededAsync(sourceEventId, context, cancellationToken);
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
