using System.Text.Json;
using FluentAssertions;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Exceptions;
using VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;
using VietRide.Payment.Application.Features.Payments.GetPaymentSessionStatus;
using VietRide.Payment.Application.Features.Payments.GetVnPayMobileSdkReturn;
using VietRide.Payment.Application.Features.TopUps.CreateTopUp;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.UnitTests.Features.Payments;

public sealed class VnPayMobileSdkFlowTests
{
    [Fact]
    public async Task MobileReturn_WhenSignedSuccess_ReturnsSdkSuccessWithoutMutatingPayment()
    {
        var payment = CreatePayment(VnPayReturnMode.MOBILE_SDK);
        var handler = CreateReturnHandler(payment);

        var result = await handler.Handle(
            new GetVnPayMobileSdkReturnQuery(Parameters(payment, "00", "00")),
            CancellationToken.None);

        result.RedirectUri.Should().Be("http://success.sdk.merchantbackapp");
        payment.Status.Should().Be(PaymentStatus.PENDING_REDIRECT);
    }

    [Fact]
    public async Task MobileReturn_WhenProviderResponseIsCancel_ReturnsSdkCancel()
    {
        var payment = CreatePayment(VnPayReturnMode.MOBILE_SDK);
        var handler = CreateReturnHandler(payment);

        var result = await handler.Handle(
            new GetVnPayMobileSdkReturnQuery(Parameters(payment, "24", "02")),
            CancellationToken.None);

        result.RedirectUri.Should().Be("http://cancel.sdk.merchantbackapp");
    }

    [Fact]
    public async Task MobileReturn_WhenSignedTopUpSuccess_ReturnsSdkSuccess()
    {
        var topUp = TopUpRequest.Create(
            Guid.NewGuid(),
            Money.FromRaw(150_000),
            Guid.NewGuid().ToString("D"),
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
            VnPayReturnMode.MOBILE_SDK);
        var handler = new GetVnPayMobileSdkReturnQueryHandler(
            new FakeVnPayClient(),
            new FakePaymentRepository(),
            new FakeTopUpRequestRepository(topUp));

        var result = await handler.Handle(
            new GetVnPayMobileSdkReturnQuery(Parameters(topUp.VnPayTxnRef, "00", "00")),
            CancellationToken.None);

        result.RedirectUri.Should().Be("http://success.sdk.merchantbackapp");
        topUp.Status.Should().Be(TopUpRequestStatus.PENDING);
    }

    [Fact]
    public async Task MobileReturn_WhenAmountDoesNotMatch_IsRejectedWithBadRequest()
    {
        var payment = CreatePayment(VnPayReturnMode.MOBILE_SDK);
        var parameters = Parameters(payment, "00", "00");
        parameters["vnp_Amount"] = "9990000";
        var handler = CreateReturnHandler(payment);

        var act = () => handler.Handle(
            new GetVnPayMobileSdkReturnQuery(parameters),
            CancellationToken.None);

        await act.Should().ThrowAsync<VnPayMobileReturnInvalidException>()
            .Where(exception => exception.StatusCode == 400
                && exception.ErrorCode == "PAYMENT_AMOUNT_INVALID");
    }

    [Fact]
    public async Task MobileReturn_WhenSessionBelongsToWebMode_IsRejected()
    {
        var payment = CreatePayment(VnPayReturnMode.OPERATOR_WEB);
        var handler = CreateReturnHandler(payment);

        var act = () => handler.Handle(
            new GetVnPayMobileSdkReturnQuery(Parameters(payment, "00", "00")),
            CancellationToken.None);

        await act.Should().ThrowAsync<VnPayMobileReturnInvalidException>()
            .Where(exception => exception.ErrorCode == "PAYMENT_RETURN_MODE_INVALID");
    }

    [Fact]
    public async Task SessionStatus_WhenPassengerOwnsMobilePayment_ReturnsNormalizedPending()
    {
        var payment = CreatePayment(VnPayReturnMode.MOBILE_SDK);
        var handler = new GetPaymentSessionStatusQueryHandler(
            new FakePaymentRepository(payment),
            new FakeTopUpRequestRepository());

        var result = await handler.Handle(
            new GetPaymentSessionStatusQuery(payment.Id, payment.UserId!.Value),
            CancellationToken.None);

        result.Should().Be(new PaymentSessionStatusResult(payment.Id, "PENDING"));
    }

    [Fact]
    public async Task SessionStatus_WhenPassengerDoesNotOwnSession_ReturnsNotFound()
    {
        var payment = CreatePayment(VnPayReturnMode.MOBILE_SDK);
        var handler = new GetPaymentSessionStatusQueryHandler(
            new FakePaymentRepository(payment),
            new FakeTopUpRequestRepository());

        var act = () => handler.Handle(
            new GetPaymentSessionStatusQuery(payment.Id, Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<CodedNotFoundException>()
            .Where(exception => exception.ErrorCode == "PAYMENT_SESSION_NOT_FOUND");
    }

    [Fact]
    public async Task SessionStatus_WhenPassengerOwnsMobileTopUp_ReturnsNormalizedPending()
    {
        var topUp = TopUpRequest.Create(
            Guid.NewGuid(),
            Money.FromRaw(150_000),
            Guid.NewGuid().ToString("D"),
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
            VnPayReturnMode.MOBILE_SDK);
        var handler = new GetPaymentSessionStatusQueryHandler(
            new FakePaymentRepository(),
            new FakeTopUpRequestRepository(topUp));

        var result = await handler.Handle(
            new GetPaymentSessionStatusQuery(topUp.Id, topUp.UserId),
            CancellationToken.None);

        result.Should().Be(new PaymentSessionStatusResult(topUp.Id, "PENDING"));
    }

    [Fact]
    public void PublicMobileResults_SerializeExactVnpaySdkContract()
    {
        var sdk = new VnPaySdkConfiguration("TESTTMN", "vietride", true);
        object[] results =
        [
            new CreateTopUpResult(Guid.NewGuid(), "PENDING", "https://pay.test", "MOBILE_SDK", sdk),
            new ChargePaymentResult(Guid.NewGuid(), "PENDING_REDIRECT", "https://pay.test", null, "MOBILE_SDK", sdk),
        ];

        foreach (var result in results)
            AssertVnPaySdkJson(result);
    }

    private static GetVnPayMobileSdkReturnQueryHandler CreateReturnHandler(PaymentEntity payment)
        => new(
            new FakeVnPayClient(),
            new FakePaymentRepository(payment),
            new FakeTopUpRequestRepository());

    private static PaymentEntity CreatePayment(VnPayReturnMode returnMode)
        => PaymentEntity.CreatePendingRedirectVnPayBooking(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.FromRaw(150_000),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
            returnMode);

    private static Dictionary<string, string> Parameters(
        PaymentEntity payment,
        string responseCode,
        string transactionStatus)
        => Parameters(payment.VnPayTxnRef!, responseCode, transactionStatus);

    private static Dictionary<string, string> Parameters(
        string txnRef,
        string responseCode,
        string transactionStatus) => new()
        {
            ["vnp_TmnCode"] = "TESTTMN",
            ["vnp_TxnRef"] = txnRef,
            ["vnp_Amount"] = "15000000",
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_TransactionStatus"] = transactionStatus,
            ["vnp_SecureHash"] = "signed",
        };

    private static void AssertVnPaySdkJson(object result)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            result,
            result.GetType(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        json.RootElement.TryGetProperty("vnpaySdk", out var sdk).Should().BeTrue();
        json.RootElement.TryGetProperty("vnPaySdk", out _).Should().BeFalse();
        sdk.GetProperty("tmnCode").GetString().Should().Be("TESTTMN");
        sdk.GetProperty("scheme").GetString().Should().Be("vietride");
        sdk.GetProperty("isSandbox").GetBoolean().Should().BeTrue();
    }

    private sealed class FakeVnPayClient : IVnPayClient
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
    }

    private sealed class FakeTopUpRequestRepository(params TopUpRequest[] topUps) : ITopUpRequestRepository
    {
        private readonly List<TopUpRequest> _topUps = topUps.ToList();

        public Task<TopUpRequest?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_topUps.FirstOrDefault(topUp => topUp.Id == id));

        public Task<TopUpRequest> AddAsync(TopUpRequest entity, CancellationToken ct)
        {
            _topUps.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(TopUpRequest entity) { }
        public void Remove(TopUpRequest entity) => _topUps.Remove(entity);
        public IQueryable<TopUpRequest> Query() => _topUps.AsQueryable();
        public IQueryable<TopUpRequest> QueryNoTracking() => _topUps.AsQueryable();
        public Task<TopUpRequest?> FindByVnPayTxnRefAsync(string vnPayTxnRef, CancellationToken cancellationToken)
            => Task.FromResult(_topUps.FirstOrDefault(topUp => topUp.VnPayTxnRef == vnPayTxnRef));
    }

    private sealed class FakePaymentRepository(params PaymentEntity[] payments) : IPaymentRepository
    {
        private readonly List<PaymentEntity> _payments = payments.ToList();

        public Task<PaymentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(_payments.FirstOrDefault(payment => payment.Id == id));

        public Task<PaymentEntity> AddAsync(PaymentEntity entity, CancellationToken cancellationToken)
        {
            _payments.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(PaymentEntity entity) { }
        public void Remove(PaymentEntity entity) => _payments.Remove(entity);
        public IQueryable<PaymentEntity> Query() => _payments.AsQueryable();
        public IQueryable<PaymentEntity> QueryNoTracking() => _payments.AsQueryable();
        public Task<PaymentEntity?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
            => Task.FromResult(_payments.FirstOrDefault(payment => payment.IdempotencyKey == idempotencyKey));
        public Task<PaymentEntity?> FindByReferenceAsync(PaymentReferenceType referenceType, Guid referenceId, CancellationToken cancellationToken)
            => Task.FromResult(_payments.FirstOrDefault(payment => payment.ReferenceType == referenceType && payment.ReferenceId == referenceId));
        public Task AcquirePaymentReferenceLockAsync(PaymentReferenceType referenceType, Guid referenceId, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task<WalletTransaction> DebitWalletBookingPaymentAsync(Guid userId, Guid bookingId, Money amount, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public Task<WalletTransaction> DebitWalletPaymentAsync(Guid userId, Guid referenceId, Money amount, WalletTransactionRef walletRef, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<PaymentEntity>> ExpirePendingRedirectDueAsync(DateTimeOffset legacyCreatedAtOrBefore, DateTimeOffset expiredAt, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PaymentEntity>>([]);
        public Task<bool> TryMarkRefundedByReferenceAsync(PaymentReferenceType referenceType, Guid referenceId, DateTimeOffset refundedAt, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }
}
