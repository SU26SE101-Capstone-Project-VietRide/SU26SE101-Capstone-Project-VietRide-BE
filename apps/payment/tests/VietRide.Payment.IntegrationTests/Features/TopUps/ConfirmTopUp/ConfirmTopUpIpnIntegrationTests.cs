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

namespace VietRide.Payment.IntegrationTests.Features.TopUps.ConfirmTopUp;

public sealed class ConfirmTopUpIpnIntegrationTests
{
    [Fact]
    public async Task PostVnPayTopUpIpn_WhenReplayed_CreditsWalletOnlyOnceAndReturnsRawVnPayBody()
    {
        using var factory = new ConfirmTopUpWebApplicationFactory();
        using var client = factory.CreateClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = factory.TxnRef,
            ["vnp_ResponseCode"] = "00",
            ["vnp_Amount"] = "10000000",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_SecureHash"] = "valid",
        });

        var first = await client.PostAsync("/v1/payments/vnpay-topup-ipn", form);
        var second = await client.PostAsync(
            "/v1/payments/vnpay-topup-ipn",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["vnp_TxnRef"] = factory.TxnRef,
                ["vnp_ResponseCode"] = "00",
                ["vnp_Amount"] = "10000000",
                ["vnp_TransactionStatus"] = "00",
                ["vnp_SecureHash"] = "valid",
            }));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(firstBody);
        doc.RootElement.GetProperty("RspCode").GetString().Should().Be("00");
        doc.RootElement.GetProperty("Message").GetString().Should().Be("Confirm Success");
        doc.RootElement.TryGetProperty("success", out _).Should().BeFalse("VNPay IPN must bypass ApiResponse envelope");

        factory.Wallets.CreditCount.Should().Be(1);
        factory.Wallets.Wallet!.Balance.Should().Be(Money.FromRaw(125_000));
        factory.Wallets.Transactions.Should().ContainSingle(tx =>
            tx.BalanceBefore == Money.FromRaw(25_000)
            && tx.BalanceAfter == Money.FromRaw(125_000)
            && tx.ReferenceType == WalletTransactionRef.TOP_UP);
        factory.Outbox.Events.Should().ContainSingle(evt => evt.EventType == "payment.wallet.credited");
        factory.VnPay.ReservedTxnRefs.Should().HaveCount(2);
        factory.VnPay.ReleasedTxnRefs.Should().HaveCount(2);
    }

    private sealed class ConfirmTopUpWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly TopUpRequest _topUp;

        public ConfirmTopUpWebApplicationFactory()
        {
            UserId = Guid.NewGuid();
            TxnRef = Guid.NewGuid().ToString("D");
            _topUp = TopUpRequest.Create(UserId, Money.FromRaw(100_000), TxnRef);
            TopUps = new FakeTopUpRequestRepository(_topUp);
            Wallets = new FakeWalletRepository(UserId, Money.FromRaw(25_000));
            VnPay = new FakeVnPayClient();
            Outbox = new FakeIntegrationEventOutbox();
        }

        public Guid UserId { get; }
        public string TxnRef { get; }
        public FakeTopUpRequestRepository TopUps { get; }
        public FakeWalletRepository Wallets { get; }
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
                services.RemoveAll<ITopUpRequestRepository>();
                services.RemoveAll<IWalletRepository>();
                services.RemoveAll<IIntegrationEventOutbox>();
                services.RemoveAll<IUnitOfWork>();
                services.AddSingleton<IVnPayClient>(VnPay);
                services.AddSingleton<ITopUpRequestRepository>(TopUps);
                services.AddSingleton<IWalletRepository>(Wallets);
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

    private sealed class FakeTopUpRequestRepository : ITopUpRequestRepository
    {
        private readonly List<TopUpRequest> _topUps;

        public FakeTopUpRequestRepository(params TopUpRequest[] topUps)
        {
            _topUps = topUps.ToList();
        }

        public Task<TopUpRequest?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_topUps.FirstOrDefault(x => x.Id == id));

        public Task<TopUpRequest> AddAsync(TopUpRequest entity, CancellationToken ct)
        {
            _topUps.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(TopUpRequest entity)
        {
        }

        public void Remove(TopUpRequest entity)
            => _topUps.Remove(entity);

        public IQueryable<TopUpRequest> Query()
            => _topUps.AsQueryable();

        public IQueryable<TopUpRequest> QueryNoTracking()
            => _topUps.AsQueryable();

        public Task<TopUpRequest?> FindByVnPayTxnRefAsync(string vnPayTxnRef, CancellationToken cancellationToken)
            => Task.FromResult(_topUps.FirstOrDefault(x => x.VnPayTxnRef == vnPayTxnRef));

        public Task<TopUpRequest?> FindPendingByVnPayTxnRefForUpdateAsync(
            string vnPayTxnRef,
            CancellationToken cancellationToken)
            => Task.FromResult(_topUps.FirstOrDefault(x =>
                x.VnPayTxnRef == vnPayTxnRef && x.Status == TopUpRequestStatus.PENDING));
    }

    private sealed class FakeWalletRepository : IWalletRepository
    {
        public FakeWalletRepository(Guid userId, Money balance)
        {
            Wallet = Wallet.Create(userId);
            if (balance.Amount > 0)
            {
                Wallet.Credit(balance);
            }
        }

        public Wallet? Wallet { get; }
        public List<WalletTransaction> Transactions { get; } = [];
        public int CreditCount { get; private set; }

        public Task<Wallet?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Wallet is not null && Wallet.UserId == id ? Wallet : null);

        public Task<Wallet> AddAsync(Wallet entity, CancellationToken ct)
            => Task.FromResult(entity);

        public void Update(Wallet entity)
        {
        }

        public void Remove(Wallet entity)
        {
        }

        public IQueryable<Wallet> Query()
            => Wallet is null ? Enumerable.Empty<Wallet>().AsQueryable() : new[] { Wallet }.AsQueryable();

        public IQueryable<Wallet> QueryNoTracking()
            => Query();

        public Task<bool> EnsureBootstrapWalletAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<WalletTransaction> CreditTopUpAsync(
            Guid userId,
            Money amount,
            Guid topUpRequestId,
            CancellationToken cancellationToken)
        {
            var wallet = Wallet ?? throw new InvalidOperationException("Wallet missing.");
            var before = wallet.Balance;
            wallet.Credit(amount);
            var transaction = WalletTransaction.Create(
                userId,
                WalletTransactionType.CREDIT,
                amount,
                before,
                wallet.Balance,
                WalletTransactionRef.TOP_UP,
                topUpRequestId,
                "VNPay wallet top-up");
            Transactions.Add(transaction);
            CreditCount++;
            return Task.FromResult(transaction);
        }
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
