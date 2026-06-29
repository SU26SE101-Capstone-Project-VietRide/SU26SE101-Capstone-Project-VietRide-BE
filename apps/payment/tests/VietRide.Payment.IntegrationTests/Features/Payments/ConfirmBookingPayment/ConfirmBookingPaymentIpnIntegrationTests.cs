using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.IntegrationTests.Features.Payments.ConfirmBookingPayment;

public sealed class ConfirmBookingPaymentIpnIntegrationTests
{
    [Fact]
    public async Task PostVnPayBookingIpn_WhenReplayed_CreditsPlatformOnlyOnceAndReturnsRawVnPayBody()
    {
        using var factory = new ConfirmBookingPaymentWebApplicationFactory();
        using var client = factory.CreateClient();

        var first = await client.PostAsync("/v1/payments/vnpay-ipn", CreateForm(factory.TxnRef));
        var second = await client.PostAsync("/v1/payments/vnpay-ipn", CreateForm(factory.TxnRef));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(firstBody);
        doc.RootElement.GetProperty("RspCode").GetString().Should().Be("00");
        doc.RootElement.GetProperty("Message").GetString().Should().Be("Confirm Success");
        doc.RootElement.TryGetProperty("success", out _).Should().BeFalse("VNPay IPN must bypass ApiResponse envelope");

        factory.Payment.Status.Should().Be(PaymentStatus.SUCCEEDED);
        factory.PlatformWallets.Balance.Should().Be(Money.FromRaw(1_250_000));
        factory.PlatformWallets.Transactions.Should().ContainSingle(tx =>
            tx.ReferenceType == PlatformWalletTransactionRef.BOOKING_PAYMENT_HOLD
            && tx.ReferenceId == factory.BookingId);
        factory.Outbox.Events.Should().ContainSingle(evt => evt.EventType == "payment.payment.succeeded");
        factory.VnPay.ReservedTxnRefs.Should().HaveCount(2);
        factory.VnPay.ReleasedTxnRefs.Should().BeEmpty();
    }

    private static FormUrlEncodedContent CreateForm(string txnRef)
        => new(new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = txnRef,
            ["vnp_ResponseCode"] = "00",
            ["vnp_Amount"] = "25000000",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_SecureHash"] = "valid",
        });

    private sealed class ConfirmBookingPaymentWebApplicationFactory : WebApplicationFactory<Program>
    {
        public ConfirmBookingPaymentWebApplicationFactory()
        {
            UserId = Guid.NewGuid();
            BookingId = Guid.NewGuid();
            TxnRef = Guid.NewGuid().ToString("D");
            Payment = PaymentEntity.CreatePendingRedirectVnPayBooking(
                BookingId,
                UserId,
                Money.FromRaw(250_000),
                TxnRef,
                "idem-key",
                "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html");
            Payments = new FakePaymentRepository(Payment);
            PlatformWallets = new FakePlatformWalletRepository(Money.FromRaw(1_000_000));
            VnPay = new FakeVnPayClient();
            Outbox = new FakeIntegrationEventOutbox();
        }

        public Guid UserId { get; }
        public Guid BookingId { get; }
        public string TxnRef { get; }
        public PaymentEntity Payment { get; }
        public FakePaymentRepository Payments { get; }
        public FakePlatformWalletRepository PlatformWallets { get; }
        public FakeVnPayClient VnPay { get; }
        public FakeIntegrationEventOutbox Outbox { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", "test-secret-at-least-32-chars-long-xxxxx");
            builder.UseSetting("INTERNAL_JWT_SECRET", "test-secret-at-least-32-chars-long-xxxxx");
            builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IVnPayClient>();
                services.RemoveAll<IPaymentRepository>();
                services.RemoveAll<IPlatformWalletRepository>();
                services.RemoveAll<IIntegrationEventOutbox>();
                services.RemoveAll<IUnitOfWork>();
                services.AddSingleton<IVnPayClient>(VnPay);
                services.AddSingleton<IPaymentRepository>(Payments);
                services.AddSingleton<IPlatformWalletRepository>(PlatformWallets);
                services.AddSingleton<IIntegrationEventOutbox>(Outbox);
            });
        }
    }

    private sealed class FakeVnPayClient : IVnPayClient
    {
        private readonly HashSet<string> _reserved = new(StringComparer.Ordinal);

        public List<string> ReservedTxnRefs { get; } = [];

        public List<string> ReleasedTxnRefs { get; } = [];

        public string CreateTopUpRedirectUrl(
            Guid userId,
            Money amount,
            string vnPayTxnRef,
            string clientIpAddress,
            DateTimeOffset createdAt)
            => throw new NotSupportedException("IPN integration tests do not create redirects.");

        public bool VerifySignature(IReadOnlyDictionary<string, string> parameters)
            => parameters.TryGetValue("vnp_SecureHash", out var hash) && hash == "valid";

        public Task<bool> TryReserveIpnAsync(string vnPayTxnRef, CancellationToken cancellationToken)
        {
            ReservedTxnRefs.Add(vnPayTxnRef);
            return Task.FromResult(_reserved.Add(vnPayTxnRef));
        }

        public Task ReleaseIpnReservationAsync(string vnPayTxnRef, CancellationToken cancellationToken)
        {
            ReleasedTxnRefs.Add(vnPayTxnRef);
            _reserved.Remove(vnPayTxnRef);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly List<PaymentEntity> _payments;

        public FakePaymentRepository(params PaymentEntity[] payments)
        {
            _payments = payments.ToList();
        }

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
            => throw new NotSupportedException("Booking IPN tests do not debit user wallets.");

        public Task<WalletTransaction> DebitWalletPaymentAsync(
            Guid userId, Guid referenceId, Money amount, WalletTransactionRef walletRef, CancellationToken ct)
            => throw new NotSupportedException("Booking IPN tests do not debit user wallets.");

        public Task<IReadOnlyList<PaymentEntity>> ExpirePendingRedirectOlderThanAsync(
            DateTimeOffset expiresBefore,
            DateTimeOffset expiredAt,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PaymentEntity>>([]);

        public Task<bool> TryMarkRefundedByReferenceAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            DateTimeOffset refundedAt,
            CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class FakePlatformWalletRepository : IPlatformWalletRepository
    {
        private readonly List<PlatformWalletTransaction> _transactions = [];

        public FakePlatformWalletRepository(Money balance)
        {
            Balance = balance;
        }

        public Money Balance { get; private set; }
        public IReadOnlyList<PlatformWalletTransaction> Transactions => _transactions;

        public Task<PlatformWallet?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<PlatformWallet?>(null);

        public Task<PlatformWallet> AddAsync(PlatformWallet entity, CancellationToken ct)
            => Task.FromResult(entity);

        public void Update(PlatformWallet entity)
        {
        }

        public void Remove(PlatformWallet entity)
        {
        }

        public IQueryable<PlatformWallet> Query()
            => Array.Empty<PlatformWallet>().AsQueryable();

        public IQueryable<PlatformWallet> QueryNoTracking()
            => Array.Empty<PlatformWallet>().AsQueryable();

        public Task<PlatformWallet> GetSingletonAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException("Booking IPN tests use credit only.");

        public Task<PlatformWalletTransaction> CreditAsync(
            Money amount,
            PlatformWalletTransactionRef referenceType,
            Guid? referenceId,
            string? note,
            CancellationToken cancellationToken)
        {
            var before = Balance;
            Balance += amount;
            var transaction = PlatformWalletTransaction.Create(
                PlatformWalletTransactionType.CREDIT,
                amount,
                before,
                Balance,
                referenceType,
                referenceId,
                note);
            _transactions.Add(transaction);
            return Task.FromResult(transaction);
        }

        public Task<PlatformWalletTransaction> DebitAsync(
            Money amount,
            PlatformWalletTransactionRef referenceType,
            Guid? referenceId,
            string? note,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Booking IPN tests do not debit platform wallet.");
    }

    private sealed class FakeIntegrationEventOutbox : IIntegrationEventOutbox
    {
        public List<(string EventType, string Payload)> Events { get; } = [];

        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
        {
            Events.Add((eventType, payloadJson));
            return Task.CompletedTask;
        }
    }
}
