using System.Text.Json;
using FluentAssertions;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Internal.Payments.CreateSubscriptionPayment;
using VietRide.Payment.Application.Features.Payments.ConfirmSubscriptionPayment;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.UnitTests.Features.Internal.Payments.CreateSubscriptionPayment;

public sealed class CreateSubscriptionPaymentCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WalletSuccess_MovesMoneyPersistsContextAndEnqueuesCanonicalEvent()
    {
        var fixture = new Fixture(700_000);

        var result = await fixture.Handler.Handle(fixture.Command("WALLET"), CancellationToken.None);

        result.Status.Should().Be("SUCCEEDED");
        result.InvoiceStatus.Should().Be("PENDING");
        result.PaymentRedirectUrl.Should().BeNull();
        fixture.OperatorWallet.Balance.Amount.Should().Be(200_000);
        fixture.OperatorTransactions.Items.Should().ContainSingle(transaction =>
            transaction.Type == OperatorWalletTransactionType.DEBIT
            && transaction.ReferenceType == OperatorWalletTransactionRef.SUBSCRIPTION_PAYMENT
            && transaction.ReferenceId == result.PaymentId);
        fixture.PlatformWallets.Transactions.Should().ContainSingle(transaction =>
            transaction.Type == PlatformWalletTransactionType.CREDIT
            && transaction.ReferenceType == PlatformWalletTransactionRef.SUBSCRIPTION_PAYMENT
            && transaction.ReferenceId == result.PaymentId);
        var payment = fixture.Payments.Items.Should().ContainSingle().Subject;
        payment.Context.Should().NotBe("{}");
        var stored = SubscriptionPaymentContextCodec.DeserializeTrusted(payment.Context);
        stored.PlanName.Should().Be("Pro");
        stored.BuyerSnapshot.TaxCode.Should().Be("0312345678");
        using var payload = JsonDocument.Parse(fixture.Outbox.Events.Should().ContainSingle().Subject.Payload);
        payload.RootElement.GetProperty("method").GetString().Should().Be("WALLET");
        payload.RootElement.GetProperty("operatorSubscriptionId").GetGuid().Should().Be(fixture.SubscriptionId);
        var buyerSnapshot = payload.RootElement.GetProperty("buyerSnapshot");
        buyerSnapshot.GetProperty("name").GetString().Should().Be("VietRide Bus");
        buyerSnapshot.TryGetProperty("addressDistrict", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WalletProratedAmount_PreservesExactDongAmount()
    {
        const long proratedAmount = 77_205_356;
        var fixture = new Fixture(100_000_000);

        var result = await fixture.Handler.Handle(
            fixture.Command("WALLET", amount: proratedAmount),
            CancellationToken.None);

        result.Status.Should().Be("SUCCEEDED");
        fixture.OperatorWallet.Balance.Amount.Should().Be(22_794_644);
        fixture.Payments.Items.Should().ContainSingle()
            .Which.Amount.Amount.Should().Be(proratedAmount);
        fixture.OperatorTransactions.Items.Should().ContainSingle()
            .Which.Amount.Amount.Should().Be(proratedAmount);
        fixture.PlatformWallets.Transactions.Should().ContainSingle()
            .Which.Amount.Amount.Should().Be(proratedAmount);
    }

    [Fact]
    public async Task Handle_WalletInsufficient_DoesNotCreatePaymentOrMoveMoney()
    {
        var fixture = new Fixture(100_000);

        var act = () => fixture.Handler.Handle(fixture.Command("WALLET"), CancellationToken.None);

        var error = await act.Should().ThrowAsync<WalletInsufficientBalanceException>();
        error.Which.StatusCode.Should().Be(402);
        fixture.Payments.Items.Should().BeEmpty();
        fixture.OperatorWallet.Balance.Amount.Should().Be(100_000);
        fixture.OperatorTransactions.Items.Should().BeEmpty();
        fixture.PlatformWallets.Transactions.Should().BeEmpty();
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TrustedPeriodExceedsBillingUpperBound_CreatesNoFinancialRecords()
    {
        var fixture = new Fixture(700_000);
        var command = fixture.Command("WALLET");
        var invalid = command with
        {
            Context = command.Context with
            {
                PeriodTo = command.Context.PeriodFrom.AddMonths(1).AddTicks(1),
            },
        };

        var action = () => fixture.Handler.Handle(invalid, CancellationToken.None);

        var error = await action.Should().ThrowAsync<CodedValidationException>();
        error.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        fixture.Payments.Items.Should().BeEmpty();
        fixture.OperatorWallet.Balance.Amount.Should().Be(700_000);
        fixture.OperatorTransactions.Items.Should().BeEmpty();
        fixture.PlatformWallets.Transactions.Should().BeEmpty();
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SameKeyReplay_DoesNotDebitTwice()
    {
        var fixture = new Fixture(700_000);

        var first = await fixture.Handler.Handle(fixture.Command("WALLET"), CancellationToken.None);
        var replay = await fixture.Handler.Handle(fixture.Command("WALLET"), CancellationToken.None);

        replay.PaymentId.Should().Be(first.PaymentId);
        fixture.OperatorWallet.Balance.Amount.Should().Be(200_000);
        fixture.OperatorTransactions.Items.Should().ContainSingle();
        fixture.PlatformWallets.Transactions.Should().ContainSingle();
        fixture.Outbox.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_KeyReplayWithDifferentMethod_RejectsMismatch()
    {
        var fixture = new Fixture(700_000);
        await fixture.Handler.Handle(fixture.Command("WALLET"), CancellationToken.None);

        var act = () => fixture.Handler.Handle(fixture.Command("VNPAY"), CancellationToken.None);

        var error = await act.Should().ThrowAsync<CodedValidationException>();
        error.Which.ErrorCode.Should().Be("IDEMPOTENCY_KEY_MISMATCH");
        fixture.OperatorWallet.Balance.Amount.Should().Be(200_000);
    }

    [Fact]
    public async Task Handle_VnPayThenConfirm_UsesSameContextAndCanonicalEventSchema()
    {
        var fixture = new Fixture(700_000);
        var created = await fixture.Handler.Handle(fixture.Command("VNPAY"), CancellationToken.None);
        var payment = fixture.Payments.Items.Single();

        var confirm = new ConfirmSubscriptionPaymentCommandHandler(
            fixture.VnPay,
            fixture.Payments,
            fixture.PlatformWallets,
            fixture.Outbox,
            new FrozenClock(Now));
        await confirm.Handle(
            new ConfirmSubscriptionPaymentCommand(new Dictionary<string, string>
            {
                ["vnp_TxnRef"] = payment.VnPayTxnRef!,
                ["vnp_ResponseCode"] = "00",
                ["vnp_Amount"] = "50000000",
            }),
            CancellationToken.None);

        created.Status.Should().Be("PENDING_REDIRECT");
        payment.Status.Should().Be(PaymentStatus.SUCCEEDED);
        SubscriptionPaymentContextCodec.DeserializeTrusted(payment.Context).PlanName.Should().Be("Pro");
        using var payload = JsonDocument.Parse(fixture.Outbox.Events.Should().ContainSingle().Subject.Payload);
        payload.RootElement.GetProperty("method").GetString().Should().Be("VNPAY");
        payload.RootElement.GetProperty("billingPeriod").GetString().Should().Be("MONTHLY");
        payload.RootElement.GetProperty("periodFrom").GetDateTimeOffset().Should().Be(Now);
        fixture.VnPay.ReleasedTxnRefs.Should().ContainSingle().Which.Should().Be(payment.VnPayTxnRef);
    }

    [Fact]
    public async Task Handle_VnPayConfirmationWhileReservationIsHeld_ReturnsRetryableFailure()
    {
        var fixture = new Fixture(700_000);
        await fixture.Handler.Handle(fixture.Command("VNPAY"), CancellationToken.None);
        var payment = fixture.Payments.Items.Single();
        fixture.VnPay.Reserve(payment.VnPayTxnRef!);
        var confirm = new ConfirmSubscriptionPaymentCommandHandler(
            fixture.VnPay,
            fixture.Payments,
            fixture.PlatformWallets,
            fixture.Outbox,
            new FrozenClock(Now));

        var result = await confirm.Handle(
            new ConfirmSubscriptionPaymentCommand(new Dictionary<string, string>
            {
                ["vnp_TxnRef"] = payment.VnPayTxnRef!,
                ["vnp_ResponseCode"] = "00",
                ["vnp_Amount"] = "50000000",
            }),
            CancellationToken.None);

        result.RspCode.Should().Be("99");
        payment.Status.Should().Be(PaymentStatus.PENDING_REDIRECT);
        fixture.PlatformWallets.Transactions.Should().BeEmpty();
        fixture.Outbox.Events.Should().BeEmpty();
        fixture.VnPay.ReleasedTxnRefs.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_FailedVnPaySession_AllowsNewSequentialSessionWithNewKey()
    {
        var fixture = new Fixture(700_000);
        var first = await fixture.Handler.Handle(fixture.Command("VNPAY"), CancellationToken.None);
        var failedPayment = fixture.Payments.Items.Single();
        var confirm = new ConfirmSubscriptionPaymentCommandHandler(
            fixture.VnPay,
            fixture.Payments,
            fixture.PlatformWallets,
            fixture.Outbox,
            new FrozenClock(Now));

        await confirm.Handle(
            new ConfirmSubscriptionPaymentCommand(new Dictionary<string, string>
            {
                ["vnp_TxnRef"] = failedPayment.VnPayTxnRef!,
                ["vnp_ResponseCode"] = "24",
                ["vnp_Amount"] = "50000000",
            }),
            CancellationToken.None);
        var retry = await fixture.Handler.Handle(fixture.Command("VNPAY", "idem-retry"), CancellationToken.None);

        failedPayment.Status.Should().Be(PaymentStatus.FAILED);
        retry.PaymentId.Should().NotBe(first.PaymentId);
        fixture.Payments.Items.Should().HaveCount(2);
        fixture.Payments.Items.Count(payment => payment.Status == PaymentStatus.PENDING_REDIRECT).Should().Be(1);
        fixture.Outbox.Events.Should().ContainSingle(evt => evt.EventType == "payment.subscription.payment_failed");
    }

    private sealed class Fixture
    {
        public Guid UpgradeAttemptId { get; } = Guid.NewGuid();
        public Guid SubscriptionId { get; } = Guid.NewGuid();
        public Guid OperatorId { get; } = Guid.NewGuid();
        public Guid PlanId { get; } = Guid.NewGuid();
        public FakePaymentRepository Payments { get; } = new();
        public OperatorWallet OperatorWallet { get; }
        public FakeOperatorWalletTransactionRepository OperatorTransactions { get; } = new();
        public FakePlatformWalletRepository PlatformWallets { get; } = new();
        public FakeVnPayClient VnPay { get; } = new();
        public FakeOutbox Outbox { get; } = new();
        public CreateSubscriptionPaymentCommandHandler Handler { get; }

        public Fixture(long operatorBalance)
        {
            OperatorWallet = OperatorWallet.Create(OperatorId);
            if (operatorBalance > 0)
                OperatorWallet.Credit(Money.FromRaw(operatorBalance));
            Handler = new CreateSubscriptionPaymentCommandHandler(
                Payments,
                new FakeOperatorWalletRepository(OperatorWallet),
                OperatorTransactions,
                PlatformWallets,
                VnPay,
                Outbox,
                new FrozenClock(Now));
        }

        public CreateSubscriptionPaymentCommand Command(
            string method,
            string idempotencyKey = "idem-subscription",
            long amount = 500_000) => new(
            UpgradeAttemptId,
            SubscriptionId,
            OperatorId,
            PlanId,
            "MONTHLY",
            method,
            amount,
            new SubscriptionPaymentContextV1(
                1,
                SubscriptionId,
                PlanId,
                "Pro",
                "MONTHLY",
                Now,
                Now.AddMonths(1),
                new SubscriptionBuyerSnapshotV1(
                    "VietRide Bus",
                    "BRN-001",
                    "0312345678",
                    "billing@vietride.test",
                    "+84901234567",
                    "1 Nguyen Hue",
                    null,
                    "Ho Chi Minh City")),
            idempotencyKey,
            "203.0.113.10",
            ReturnMode: string.Equals(method, "VNPAY", StringComparison.Ordinal)
                ? "OPERATOR_WEB"
                : null);
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        public List<PaymentEntity> Items { get; } = [];
        public Task<PaymentEntity?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
        public Task<PaymentEntity> AddAsync(PaymentEntity entity, CancellationToken ct) { Items.Add(entity); return Task.FromResult(entity); }
        public void Update(PaymentEntity entity) { }
        public void Remove(PaymentEntity entity) => Items.Remove(entity);
        public IQueryable<PaymentEntity> Query() => Items.AsQueryable();
        public IQueryable<PaymentEntity> QueryNoTracking() => Query();
        public Task<PaymentEntity?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(x => x.IdempotencyKey == key));
        public Task<PaymentEntity?> FindByReferenceAsync(PaymentReferenceType type, Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(x => x.ReferenceType == type && x.ReferenceId == id));
        public Task<PaymentEntity?> FindLatestByReferenceAsync(PaymentReferenceType type, Guid id, CancellationToken ct) => Task.FromResult(Items.LastOrDefault(x => x.ReferenceType == type && x.ReferenceId == id));
        public Task AcquirePaymentReferenceLockAsync(PaymentReferenceType type, Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task<WalletTransaction> DebitWalletBookingPaymentAsync(Guid userId, Guid bookingId, Money amount, CancellationToken ct) => throw new NotSupportedException();
        public Task<WalletTransaction> DebitWalletPaymentAsync(Guid userId, Guid id, Money amount, WalletTransactionRef walletRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PaymentEntity>> ExpirePendingRedirectDueAsync(DateTimeOffset before, DateTimeOffset at, CancellationToken ct) => Task.FromResult<IReadOnlyList<PaymentEntity>>([]);
        public Task<bool> TryMarkRefundedByReferenceAsync(PaymentReferenceType type, Guid id, DateTimeOffset at, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class FakeOperatorWalletRepository(OperatorWallet wallet) : IOperatorWalletRepository
    {
        public Task<OperatorWallet?> FindByOperatorIdAsync(Guid operatorId, CancellationToken ct) => Task.FromResult<OperatorWallet?>(operatorId == wallet.OperatorId ? wallet : null);
        public Task<OperatorWallet?> GetByIdAsync(Guid id, CancellationToken ct) => FindByOperatorIdAsync(id, ct);
        public Task<OperatorWallet> AddAsync(OperatorWallet entity, CancellationToken ct) => Task.FromResult(entity);
        public void Update(OperatorWallet entity) { }
        public void Remove(OperatorWallet entity) { }
        public IQueryable<OperatorWallet> Query() => new[] { wallet }.AsQueryable();
        public IQueryable<OperatorWallet> QueryNoTracking() => Query();
    }

    private sealed class FakeOperatorWalletTransactionRepository : IOperatorWalletTransactionRepository
    {
        public List<OperatorWalletTransaction> Items { get; } = [];
        public Task<OperatorWalletTransaction?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
        public Task<OperatorWalletTransaction> AddAsync(OperatorWalletTransaction entity, CancellationToken ct) { Items.Add(entity); return Task.FromResult(entity); }
        public void Update(OperatorWalletTransaction entity) => throw new NotSupportedException();
        public void Remove(OperatorWalletTransaction entity) => throw new NotSupportedException();
        public IQueryable<OperatorWalletTransaction> Query() => Items.AsQueryable();
        public IQueryable<OperatorWalletTransaction> QueryNoTracking() => Query();
    }

    private sealed class FakePlatformWalletRepository : IPlatformWalletRepository
    {
        private Money _balance = Money.Zero;
        public List<PlatformWalletTransaction> Transactions { get; } = [];
        public Task<PlatformWallet?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<PlatformWallet?>(null);
        public Task<PlatformWallet> AddAsync(PlatformWallet entity, CancellationToken ct) => Task.FromResult(entity);
        public void Update(PlatformWallet entity) { }
        public void Remove(PlatformWallet entity) { }
        public IQueryable<PlatformWallet> Query() => Array.Empty<PlatformWallet>().AsQueryable();
        public IQueryable<PlatformWallet> QueryNoTracking() => Query();
        public Task<PlatformWallet> GetSingletonAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<PlatformWalletTransaction> CreditAsync(Money amount, PlatformWalletTransactionRef referenceType, Guid? referenceId, string? note, CancellationToken ct)
        {
            var before = _balance;
            _balance += amount;
            var transaction = PlatformWalletTransaction.Create(PlatformWalletTransactionType.CREDIT, amount, before, _balance, referenceType, referenceId, note);
            Transactions.Add(transaction);
            return Task.FromResult(transaction);
        }
        public Task<PlatformWalletTransaction> DebitAsync(Money amount, PlatformWalletTransactionRef referenceType, Guid? referenceId, string? note, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeVnPayClient : IVnPayClient
    {
        private readonly HashSet<string> _reserved = new(StringComparer.Ordinal);

        public List<string> ReleasedTxnRefs { get; } = [];

        public void Reserve(string vnPayTxnRef) => _reserved.Add(vnPayTxnRef);

        public string CreateTopUpRedirectUrl(Guid userId, Money amount, string reference, string ip, DateTimeOffset at) => throw new NotSupportedException();
        public string CreateSubscriptionPaymentRedirectUrl(Guid attemptId, Guid operatorId, Money amount, string reference, string ip, DateTimeOffset at) => $"https://sandbox.vnpay.test/pay?ref={reference}";
        public bool VerifySignature(IReadOnlyDictionary<string, string> parameters) => true;
        public Task<bool> TryReserveIpnAsync(string vnPayTxnRef, CancellationToken cancellationToken)
            => Task.FromResult(_reserved.Add(vnPayTxnRef));

        public Task ReleaseIpnReservationAsync(string vnPayTxnRef, CancellationToken cancellationToken)
        {
            ReleasedTxnRefs.Add(vnPayTxnRef);
            _reserved.Remove(vnPayTxnRef);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOutbox : IIntegrationEventOutbox
    {
        public List<(string EventType, string Payload)> Events { get; } = [];
        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default) { Events.Add((eventType, payloadJson)); return Task.CompletedTask; }
    }

    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
