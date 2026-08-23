using System.Text.Json;
using MediatR;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.Application.Features.Internal.Payments.CreateSubscriptionPayment;

public sealed class CreateSubscriptionPaymentCommandHandler
    : IRequestHandler<CreateSubscriptionPaymentCommand, CreateSubscriptionPaymentResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IPaymentRepository _payments;
    private readonly IOperatorWalletRepository _operatorWallets;
    private readonly IOperatorWalletTransactionRepository _operatorTransactions;
    private readonly IPlatformWalletRepository _platformWallets;
    private readonly IVnPayClient _vnPayClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public CreateSubscriptionPaymentCommandHandler(
        IPaymentRepository payments,
        IOperatorWalletRepository operatorWallets,
        IOperatorWalletTransactionRepository operatorTransactions,
        IPlatformWalletRepository platformWallets,
        IVnPayClient vnPayClient,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _payments = payments;
        _operatorWallets = operatorWallets;
        _operatorTransactions = operatorTransactions;
        _platformWallets = platformWallets;
        _vnPayClient = vnPayClient;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<CreateSubscriptionPaymentResult> Handle(
        CreateSubscriptionPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var replay = await _payments.FindByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (replay is not null)
        {
            EnsureReplayMatches(replay, request);
            return ToResult(replay);
        }

        await _payments.AcquirePaymentReferenceLockAsync(
                PaymentReferenceType.SUBSCRIPTION,
                request.UpgradeAttemptId,
                cancellationToken)
            .ConfigureAwait(false);

        var existing = await _payments.FindLatestByReferenceAsync(
                PaymentReferenceType.SUBSCRIPTION,
                request.UpgradeAttemptId,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            EnsureReplayMatches(existing, request);
            if (existing.Status is PaymentStatus.PENDING_REDIRECT or PaymentStatus.SUCCEEDED)
                return ToResult(existing);
        }

        var contextJson = SubscriptionPaymentContextCodec.ValidateAndSerialize(request.Context, request.SubscriptionId);
        if (request.Context.PlanId != request.PlanId
            || request.Context.BillingPeriod != request.BillingPeriod)
        {
            throw new CodedValidationException(
                "PAYMENT_CONTEXT_INVALID",
                "Subscription payment context does not match the requested plan and billing period.");
        }

        var method = Enum.Parse<PaymentMethod>(request.PaymentMethod, ignoreCase: false);
        var returnMode = ParseReturnMode(request, method);
        var amount = Money.FromRaw(request.Amount);
        var now = _clock.UtcNow;
        var dueAt = request.DueAt ?? now.AddMinutes(15);
        if (dueAt <= now)
            throw new CodedConflictException("SUBSCRIPTION_UPGRADE_EXPIRED", "Subscription upgrade attempt has expired.");
        if (method == PaymentMethod.WALLET)
        {
            var wallet = await _operatorWallets.FindByOperatorIdAsync(request.OperatorId, cancellationToken)
                .ConfigureAwait(false);
            if (wallet is null || wallet.Balance < amount)
                throw new WalletInsufficientBalanceException();

            var walletPayment = PaymentEntity.CreatePendingRedirect(
                PaymentReferenceType.SUBSCRIPTION,
                request.UpgradeAttemptId,
                amount,
                PaymentMethod.WALLET,
                operatorId: request.OperatorId,
                idempotencyKey: request.IdempotencyKey);
            walletPayment.AttachContext(contextJson);
            walletPayment.MarkSucceeded(null, now);
            await _payments.AddAsync(walletPayment, cancellationToken).ConfigureAwait(false);

            var balanceBefore = wallet.Balance;
            wallet.Debit(amount);
            await _operatorTransactions.AddAsync(
                OperatorWalletTransaction.Create(
                    request.OperatorId,
                    OperatorWalletTransactionType.DEBIT,
                    amount,
                    balanceBefore,
                    wallet.Balance,
                    OperatorWalletTransactionRef.SUBSCRIPTION_PAYMENT,
                    walletPayment.Id,
                    "Subscription payment",
                    now),
                cancellationToken).ConfigureAwait(false);
            await _platformWallets.CreditAsync(
                amount,
                PlatformWalletTransactionRef.SUBSCRIPTION_PAYMENT,
                walletPayment.Id,
                "OperatorWallet subscription payment",
                cancellationToken).ConfigureAwait(false);
            await EnqueueSucceededAsync(walletPayment, request.Context, cancellationToken).ConfigureAwait(false);
            return ToResult(walletPayment);
        }

        var txnRef = Guid.NewGuid().ToString("D");
        var redirectUrl = _vnPayClient.CreateSubscriptionPaymentRedirectUrl(
            request.UpgradeAttemptId,
            request.OperatorId,
            amount,
            txnRef,
            request.ClientIpAddress,
            now,
            dueAt,
            returnMode!.Value);
        var payment = PaymentEntity.CreatePendingRedirectVnPaySubscription(
            request.UpgradeAttemptId,
            request.OperatorId,
            amount,
            txnRef,
            request.IdempotencyKey,
            redirectUrl,
            dueAt,
            returnMode.Value);
        payment.AttachContext(contextJson);

        await _payments.AddAsync(payment, cancellationToken).ConfigureAwait(false);
        return ToResult(payment);
    }

    private static CreateSubscriptionPaymentResult ToResult(PaymentEntity payment)
    {
        if (payment.Method == PaymentMethod.VNPAY && string.IsNullOrWhiteSpace(payment.PaymentRedirectUrl))
            throw new InvalidOperationException("Subscription payment redirect URL is missing.");

        return new CreateSubscriptionPaymentResult(
            payment.Id,
            payment.Status.ToString(),
            payment.PaymentRedirectUrl,
            payment.Status == PaymentStatus.SUCCEEDED ? "PENDING" : null);
    }

    private static void EnsureReplayMatches(PaymentEntity payment, CreateSubscriptionPaymentCommand request)
    {
        if (payment.ReferenceType != PaymentReferenceType.SUBSCRIPTION
            || payment.ReferenceId != request.UpgradeAttemptId
            || payment.OperatorId != request.OperatorId
            || payment.Amount.Amount != request.Amount
            || !string.Equals(payment.Method.ToString(), request.PaymentMethod, StringComparison.Ordinal)
            || payment.ReturnMode != ParseReturnMode(request, payment.Method))
        {
            throw new CodedValidationException(
                "IDEMPOTENCY_KEY_MISMATCH",
                "Idempotency-Key was already used with a different subscription payment request.");
        }

        var storedContext = SubscriptionPaymentContextCodec.DeserializeTrusted(payment.Context);
        if (storedContext.OperatorSubscriptionId != request.SubscriptionId
            || storedContext.PlanId != request.PlanId
            || !string.Equals(storedContext.BillingPeriod, request.BillingPeriod, StringComparison.Ordinal))
        {
            throw new CodedValidationException(
                "IDEMPOTENCY_KEY_MISMATCH",
                "Idempotency-Key was already used with a different subscription payment snapshot.");
        }
    }

    private static VnPayReturnMode? ParseReturnMode(
        CreateSubscriptionPaymentCommand request,
        PaymentMethod method)
    {
        if (method != PaymentMethod.VNPAY)
            return null;

        if (!Enum.TryParse<VnPayReturnMode>(request.ReturnMode, ignoreCase: true, out var returnMode)
            || returnMode != VnPayReturnMode.OPERATOR_WEB)
        {
            throw new CodedValidationException(
                "PAYMENT_RETURN_MODE_INVALID",
                "returnMode must be OPERATOR_WEB for VNPay subscription payments.");
        }

        return returnMode;
    }

    private Task EnqueueSucceededAsync(
        PaymentEntity payment,
        SubscriptionPaymentContextV1 context,
        CancellationToken cancellationToken)
    {
        var evt = new SubscriptionPaymentSucceededIntegrationEvent(
            payment.Id,
            payment.ReferenceId,
            payment.OperatorId ?? Guid.Empty,
            context.OperatorSubscriptionId,
            payment.Amount.Amount,
            payment.Method.ToString(),
            payment.SucceededAt ?? _clock.UtcNow,
            context);
        return _outbox.EnqueueAsync(
            evt.EventType,
            JsonSerializer.Serialize(evt, JsonOptions),
            cancellationToken);
    }
}
