using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.TopUps.ConfirmTopUp;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.UnitTests.Features.TopUps.ConfirmTopUp;

public sealed class ConfirmTopUpCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenIpnIsValid_CreditsWalletAndEnqueuesWalletCreditedEvent()
    {
        var userId = Guid.NewGuid();
        var topUp = TopUpRequest.Create(userId, Money.FromRaw(100_000), "txn-1");
        var topUps = new FakeTopUpRequestRepository(topUp);
        var wallets = new FakeWalletRepository(userId, Money.FromRaw(25_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(new FakeVnPayClient(isSignatureValid: true), topUps, wallets, outbox);

        var result = await handler.Handle(CreateCommand("txn-1", "00"), CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.RspCode.Should().Be("00");
        topUp.Status.Should().Be(TopUpRequestStatus.SUCCEEDED);
        wallets.CreditCount.Should().Be(1);
        wallets.Wallet!.Balance.Should().Be(Money.FromRaw(125_000));
        wallets.Transactions.Should().ContainSingle(tx =>
            tx.BalanceBefore == Money.FromRaw(25_000)
            && tx.BalanceAfter == Money.FromRaw(125_000)
            && tx.ReferenceType == WalletTransactionRef.TOP_UP
            && tx.ReferenceId == topUp.Id);
        outbox.Events.Should().ContainSingle(evt =>
            evt.EventType == "payment.wallet.credited"
            && evt.Payload.Contains("\"userId\"", StringComparison.Ordinal)
            && evt.Payload.Contains("\"amount\":100000", StringComparison.Ordinal)
            && evt.Payload.Contains("\"referenceType\":\"TOP_UP\"", StringComparison.Ordinal)
            && evt.Payload.Contains(topUp.Id.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_WhenSameIpnReplays_DoesNotDoubleCredit()
    {
        var userId = Guid.NewGuid();
        var topUp = TopUpRequest.Create(userId, Money.FromRaw(100_000), "txn-1");
        var wallets = new FakeWalletRepository(userId, Money.FromRaw(25_000));
        var outbox = new FakeIntegrationEventOutbox();
        var vnPay = new FakeVnPayClient(isSignatureValid: true);
        var handler = CreateHandler(vnPay, new FakeTopUpRequestRepository(topUp), wallets, outbox);

        await handler.Handle(CreateCommand("txn-1", "00"), CancellationToken.None);
        var replay = await handler.Handle(CreateCommand("txn-1", "00"), CancellationToken.None);

        replay.StatusCode.Should().Be(200);
        wallets.CreditCount.Should().Be(1);
        wallets.Wallet!.Balance.Should().Be(Money.FromRaw(125_000));
        outbox.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_WhenSignatureIsInvalid_Returns401WithoutMutatingState()
    {
        var userId = Guid.NewGuid();
        var topUp = TopUpRequest.Create(userId, Money.FromRaw(100_000), "txn-1");
        var wallets = new FakeWalletRepository(userId, Money.FromRaw(25_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(new FakeVnPayClient(isSignatureValid: false), new FakeTopUpRequestRepository(topUp), wallets, outbox);

        var result = await handler.Handle(CreateCommand("txn-1", "00"), CancellationToken.None);

        result.StatusCode.Should().Be(401);
        result.RspCode.Should().Be("97");
        result.Message.Should().Be("PAYMENT_SIGNATURE_INVALID");
        topUp.Status.Should().Be(TopUpRequestStatus.PENDING);
        wallets.CreditCount.Should().Be(0);
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenVnPayResponseCodeIsNotSuccess_MarksFailedWithoutCredit()
    {
        var userId = Guid.NewGuid();
        var topUp = TopUpRequest.Create(userId, Money.FromRaw(100_000), "txn-1");
        var wallets = new FakeWalletRepository(userId, Money.FromRaw(25_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(new FakeVnPayClient(isSignatureValid: true), new FakeTopUpRequestRepository(topUp), wallets, outbox);

        var result = await handler.Handle(CreateCommand("txn-1", "24"), CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.RspCode.Should().Be("00");
        topUp.Status.Should().Be(TopUpRequestStatus.FAILED);
        topUp.VnPayResponseCode.Should().Be("24");
        wallets.CreditCount.Should().Be(0);
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenSignedAmountDoesNotMatchTopUp_MarksFailedWithoutCreditOrOutbox()
    {
        var userId = Guid.NewGuid();
        var topUp = TopUpRequest.Create(userId, Money.FromRaw(100_000), "txn-1");
        var wallets = new FakeWalletRepository(userId, Money.FromRaw(25_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(new FakeVnPayClient(isSignatureValid: true), new FakeTopUpRequestRepository(topUp), wallets, outbox);

        var result = await handler.Handle(CreateCommand("txn-1", "00", signedAmount: "9999000"), CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.RspCode.Should().NotBe("00");
        topUp.Status.Should().Be(TopUpRequestStatus.FAILED);
        topUp.VnPayResponseCode.Should().Be("00");
        wallets.CreditCount.Should().Be(0);
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenTransactionStatusIsNotSuccess_MarksFailedWithoutCreditOrOutbox()
    {
        var userId = Guid.NewGuid();
        var topUp = TopUpRequest.Create(userId, Money.FromRaw(100_000), "txn-1");
        var wallets = new FakeWalletRepository(userId, Money.FromRaw(25_000));
        var outbox = new FakeIntegrationEventOutbox();
        var handler = CreateHandler(new FakeVnPayClient(isSignatureValid: true), new FakeTopUpRequestRepository(topUp), wallets, outbox);

        var result = await handler.Handle(CreateCommand("txn-1", "00", transactionStatus: "02"), CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.RspCode.Should().Be("00");
        topUp.Status.Should().Be(TopUpRequestStatus.FAILED);
        topUp.VnPayResponseCode.Should().Be("02");
        wallets.CreditCount.Should().Be(0);
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenProcessingFailsAfterReservation_ReleasesReservationSoRetryCanProceed()
    {
        var userId = Guid.NewGuid();
        var vnPay = new FakeVnPayClient(isSignatureValid: true);
        var failingTopUp = TopUpRequest.Create(userId, Money.FromRaw(100_000), "txn-1");
        var failingHandler = CreateHandler(
            vnPay,
            new FakeTopUpRequestRepository(failingTopUp),
            new ThrowingWalletRepository(new InvalidOperationException("wallet write failed")),
            new FakeIntegrationEventOutbox());

        await FluentActions.Invoking(() => failingHandler.Handle(CreateCommand("txn-1", "00"), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("wallet write failed");

        var retryTopUp = TopUpRequest.Create(userId, Money.FromRaw(100_000), "txn-1");
        var wallets = new FakeWalletRepository(userId, Money.FromRaw(25_000));
        var outbox = new FakeIntegrationEventOutbox();
        var retryHandler = CreateHandler(vnPay, new FakeTopUpRequestRepository(retryTopUp), wallets, outbox);

        var retryResult = await retryHandler.Handle(CreateCommand("txn-1", "00"), CancellationToken.None);

        retryResult.StatusCode.Should().Be(200);
        retryResult.RspCode.Should().Be("00");
        retryResult.Message.Should().Be("Confirm Success");
        retryTopUp.Status.Should().Be(TopUpRequestStatus.SUCCEEDED);
        wallets.CreditCount.Should().Be(1);
        wallets.Wallet!.Balance.Should().Be(Money.FromRaw(125_000));
        outbox.Events.Should().ContainSingle(evt => evt.EventType == "payment.wallet.credited");
    }

    private static ConfirmTopUpCommandHandler CreateHandler(
        IVnPayClient vnPayClient,
        ITopUpRequestRepository topUps,
        IWalletRepository wallets,
        IIntegrationEventOutbox outbox)
        => new(
            vnPayClient,
            topUps,
            wallets,
            outbox,
            new FrozenClock(new DateTimeOffset(2026, 6, 12, 10, 0, 0, TimeSpan.Zero)),
            NullLogger<ConfirmTopUpCommandHandler>.Instance);

    private static ConfirmTopUpCommand CreateCommand(
        string txnRef,
        string responseCode,
        string signedAmount = "10000000",
        string? transactionStatus = "00")
    {
        var parameters = new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = txnRef,
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_Amount"] = signedAmount,
            ["vnp_SecureHash"] = "valid",
        };

        if (transactionStatus is not null)
        {
            parameters["vnp_TransactionStatus"] = transactionStatus;
        }

        return new ConfirmTopUpCommand(parameters);
    }

    private sealed class FakeVnPayClient : IVnPayClient
    {
        private readonly bool _isSignatureValid;
        private readonly HashSet<string> _reserved = new(StringComparer.Ordinal);

        public FakeVnPayClient(bool isSignatureValid, IEnumerable<string>? reservedTxnRefs = null)
        {
            _isSignatureValid = isSignatureValid;

            if (reservedTxnRefs is not null)
            {
                foreach (var txnRef in reservedTxnRefs)
                {
                    _reserved.Add(txnRef);
                }
            }
        }

        public string CreateTopUpRedirectUrl(
            Guid userId,
            Money amount,
            string vnPayTxnRef,
            string clientIpAddress,
            DateTimeOffset createdAt)
            => throw new NotSupportedException("Confirm top-up tests do not create redirects.");

        public bool VerifySignature(IReadOnlyDictionary<string, string> parameters)
            => _isSignatureValid;

        public Task<bool> TryReserveIpnAsync(string vnPayTxnRef, CancellationToken cancellationToken)
            => Task.FromResult(_reserved.Add(vnPayTxnRef));

        public Task ReleaseIpnReservationAsync(string vnPayTxnRef, CancellationToken cancellationToken)
        {
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

    private sealed class ThrowingWalletRepository : IWalletRepository
    {
        private readonly Exception _exception;

        public ThrowingWalletRepository(Exception exception)
        {
            _exception = exception;
        }

        public Task<Wallet?> GetByIdAsync(Guid id, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<Wallet> AddAsync(Wallet entity, CancellationToken ct)
            => throw new NotSupportedException();

        public void Update(Wallet entity)
            => throw new NotSupportedException();

        public void Remove(Wallet entity)
            => throw new NotSupportedException();

        public IQueryable<Wallet> Query()
            => throw new NotSupportedException();

        public IQueryable<Wallet> QueryNoTracking()
            => throw new NotSupportedException();

        public Task<bool> EnsureBootstrapWalletAsync(Guid userId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<WalletTransaction> CreditTopUpAsync(
            Guid userId,
            Money amount,
            Guid topUpRequestId,
            CancellationToken cancellationToken)
            => Task.FromException<WalletTransaction>(_exception);
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

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
