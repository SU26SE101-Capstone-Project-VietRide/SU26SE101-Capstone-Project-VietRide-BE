using MediatR;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Subscriptions.UpgradeSubscription;

public sealed class UpgradeSubscriptionCommandHandler
    : IRequestHandler<UpgradeSubscriptionCommand, SubscriptionUpgradeResponseDto>
{
    private static readonly TimeSpan PaymentWindow = TimeSpan.FromMinutes(15);
    private const string OperatorWebReturnMode = "OPERATOR_WEB";

    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly ISubscriptionPlanRepository _plans;
    private readonly ISubscriptionUpgradeAttemptRepository _attempts;
    private readonly IOperatorRepository _operators;
    private readonly ISubscriptionPaymentClient _payments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpgradeSubscriptionCommandHandler(
        IOperatorSubscriptionRepository subscriptions,
        ISubscriptionPlanRepository plans,
        ISubscriptionUpgradeAttemptRepository attempts,
        IOperatorRepository operators,
        ISubscriptionPaymentClient payments,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _subscriptions = subscriptions;
        _plans = plans;
        _attempts = attempts;
        _operators = operators;
        _payments = payments;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<SubscriptionUpgradeResponseDto> Handle(
        UpgradeSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var targetPlan = await _plans.GetByIdAsync(request.PlanId, cancellationToken)
            ?? throw new NotFoundException(nameof(SubscriptionPlan), request.PlanId);
        if (!targetPlan.IsActive)
            throw new CodedValidationException("SUBSCRIPTION_PLAN_INACTIVE", "The selected subscription plan is inactive.");

        var billingPeriod = SubscriptionMapper.ParseBillingPeriod(request.BillingPeriod);
        var paymentMethod = Enum.Parse<SubscriptionPaymentMethod>(request.PaymentMethod, ignoreCase: false);
        var amount = billingPeriod == SubscriptionBillingPeriod.MONTHLY
            ? targetPlan.PricePerMonth
            : targetPlan.PricePerYear;
        if (amount.Amount <= 0)
            throw new CodedValidationException("SUBSCRIPTION_PLAN_NOT_PAYABLE", "The selected plan has no payable price.");

        var operatorTenant = await GetOperatorAsync(request.OperatorId, cancellationToken);
        var attempt = await GetOrCreateAttemptAsync(
            request,
            billingPeriod,
            amount,
            paymentMethod,
            cancellationToken);
        var snapshot = CreateSnapshot(
            attempt.SubscriptionId,
            targetPlan,
            billingPeriod,
            operatorTenant,
            attempt.CreatedAt == default ? _clock.UtcNow : attempt.CreatedAt);

        SubscriptionPaymentCreationResult payment;
        try
        {
            payment = await _payments.CreateAsync(
                new SubscriptionPaymentCreationRequest(
                    attempt.Id,
                    attempt.SubscriptionId,
                    request.OperatorId,
                    targetPlan.Id,
                    billingPeriod.ToString(),
                    request.PaymentMethod,
                    amount.Amount,
                    snapshot,
                    OperatorWebReturnMode,
                    request.IdempotencyKey,
                    request.ClientIpAddress,
                    attempt.DueAt),
                cancellationToken);
        }
        catch (Exception exception) when (exception is ICodedHttpException { StatusCode: >= 400 and < 500 })
        {
            await MarkAttemptFailedAsync(attempt.Id, cancellationToken);
            throw;
        }

        if (payment.Status != "SUCCEEDED")
        {
            await MoveToPendingPaymentAsync(
                attempt.SubscriptionId,
                attempt.Id,
                payment.PaymentId,
                paymentMethod,
                cancellationToken);
        }

        var activePlan = payment.Status == "SUCCEEDED"
            ? targetPlan
            : await GetActivePlanAsync(attempt.SubscriptionId, cancellationToken);

        return new SubscriptionUpgradeResponseDto(
            attempt.SubscriptionId,
            attempt.Id,
            payment.Status == "SUCCEEDED" ? SubscriptionStatus.ACTIVE.ToString() : SubscriptionStatus.PENDING_PAYMENT.ToString(),
            payment.PaymentId,
            amount.Amount,
            billingPeriod.ToString(),
            payment.PaymentRedirectUrl,
            payment.Status == "SUCCEEDED" ? null : attempt.DueAt,
            payment.InvoiceStatus,
            SubscriptionMapper.ToPlanDto(activePlan),
            payment.Status == "SUCCEEDED" ? null : SubscriptionMapper.ToPlanDto(targetPlan));
    }

    private async Task<SubscriptionUpgradeAttempt> GetOrCreateAttemptAsync(
        UpgradeSubscriptionCommand request,
        SubscriptionBillingPeriod billingPeriod,
        VietRide.Shared.Kernel.ValueObjects.Money amount,
        SubscriptionPaymentMethod paymentMethod,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var subscription = await _subscriptions.GetCurrentByOperatorIdForUpdateAsync(
                request.OperatorId,
                cancellationToken)
                ?? throw new NotFoundException(nameof(OperatorSubscription), request.OperatorId);

            var replay = await _attempts.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
            if (replay is not null)
            {
                EnsureReplayMatches(replay, request);
                await _unitOfWork.CommitAsync(cancellationToken);
                return replay;
            }

            var activeAttempt = await _attempts.GetActiveBySubscriptionIdAsync(subscription.Id, cancellationToken);
            if (activeAttempt is not null || subscription.Status == SubscriptionStatus.PENDING_PAYMENT)
            {
                throw new CodedConflictException(
                    "SUBSCRIPTION_PAYMENT_PENDING",
                    "An upgrade payment is already pending.");
            }

            if (subscription.Status is not (SubscriptionStatus.ACTIVE or SubscriptionStatus.EXPIRED))
            {
                throw new CodedConflictException(
                    "SUBSCRIPTION_NOT_UPGRADABLE",
                    "Only active or expired subscriptions can start an upgrade.");
            }

            var now = _clock.UtcNow;
            var attempt = SubscriptionUpgradeAttempt.Create(
                subscription.Id,
                request.OperatorId,
                request.PlanId,
                billingPeriod,
                amount,
                paymentMethod,
                request.IdempotencyKey,
                SubscriptionFallbackPolicy.RESTORE_CURRENT,
                now,
                now.Add(PaymentWindow));
            await _attempts.AddAsync(attempt, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
            return attempt;
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task MoveToPendingPaymentAsync(
        Guid subscriptionId,
        Guid attemptId,
        Guid paymentId,
        SubscriptionPaymentMethod paymentMethod,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var subscription = await _subscriptions.GetByIdForUpdateAsync(subscriptionId, cancellationToken)
                ?? throw new NotFoundException(nameof(OperatorSubscription), subscriptionId);
            var attempt = await _attempts.GetByIdForUpdateAsync(attemptId, cancellationToken)
                ?? throw new NotFoundException(nameof(SubscriptionUpgradeAttempt), attemptId);

            if (attempt.Status == SubscriptionUpgradeAttemptStatus.SUCCEEDED)
            {
                await _unitOfWork.CommitAsync(cancellationToken);
                return;
            }

            attempt.BindPendingPayment(paymentId);
            if (subscription.Status is SubscriptionStatus.ACTIVE or SubscriptionStatus.EXPIRED)
            {
                subscription.MoveToPendingPayment(paymentMethod);
            }
            else if (subscription.Status != SubscriptionStatus.PENDING_PAYMENT
                || subscription.PaymentMethod != paymentMethod)
            {
                throw new CodedConflictException(
                    "SUBSCRIPTION_PAYMENT_STATE_CHANGED",
                    "Subscription state changed while the payment was being initialized.");
            }

            _subscriptions.Update(subscription);
            _attempts.Update(attempt);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task MarkAttemptFailedAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var attempt = await _attempts.GetByIdForUpdateAsync(attemptId, cancellationToken);
            if (attempt is not null)
            {
                attempt.MarkFailed();
                _attempts.Update(attempt);
            }

            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void EnsureReplayMatches(SubscriptionUpgradeAttempt attempt, UpgradeSubscriptionCommand request)
    {
        if (attempt.OperatorId != request.OperatorId
            || attempt.TargetPlanId != request.PlanId
            || !string.Equals(attempt.BillingPeriod.ToString(), request.BillingPeriod, StringComparison.Ordinal)
            || !string.Equals(attempt.PaymentMethod.ToString(), request.PaymentMethod, StringComparison.Ordinal))
        {
            throw new CodedValidationException(
                "IDEMPOTENCY_KEY_MISMATCH",
                "Idempotency-Key was already used with a different subscription upgrade request.");
        }
    }

    private async Task<Operator> GetOperatorAsync(Guid operatorId, CancellationToken cancellationToken)
        => await _operators.GetByIdNoTrackingAsync(operatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Operator), operatorId);

    private async Task<SubscriptionPlan> GetActivePlanAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptions.GetByIdAsync(subscriptionId, cancellationToken)
            ?? throw new NotFoundException(nameof(OperatorSubscription), subscriptionId);
        return await _plans.GetByIdAsync(subscription.PlanId, cancellationToken)
            ?? throw new NotFoundException(nameof(SubscriptionPlan), subscription.PlanId);
    }

    private static SubscriptionPaymentSnapshot CreateSnapshot(
        Guid subscriptionId,
        SubscriptionPlan plan,
        SubscriptionBillingPeriod billingPeriod,
        Operator operatorTenant,
        DateTimeOffset periodFrom)
    {
        var periodTo = billingPeriod == SubscriptionBillingPeriod.MONTHLY
            ? periodFrom.AddMonths(1)
            : periodFrom.AddYears(1);
        return new SubscriptionPaymentSnapshot(
            1,
            subscriptionId,
            plan.Id,
            plan.Name,
            billingPeriod.ToString(),
            periodFrom,
            periodTo,
            new SubscriptionBuyerSnapshot(
                operatorTenant.Name,
                operatorTenant.BusinessRegistrationNumber,
                operatorTenant.TaxCode,
                operatorTenant.ContactEmail,
                operatorTenant.ContactPhone,
                operatorTenant.AddressStreet,
                operatorTenant.AddressWard,
                operatorTenant.AddressDistrict,
                operatorTenant.AddressProvince));
    }
}
