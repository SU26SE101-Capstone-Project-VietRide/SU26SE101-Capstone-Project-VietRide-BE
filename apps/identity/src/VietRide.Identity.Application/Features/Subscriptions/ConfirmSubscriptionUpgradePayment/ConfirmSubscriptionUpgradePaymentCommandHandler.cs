using MediatR;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Subscriptions.QuoteSubscriptionUpgrade;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Subscriptions.ConfirmSubscriptionUpgradePayment;

public sealed class ConfirmSubscriptionUpgradePaymentCommandHandler
    : IRequestHandler<ConfirmSubscriptionUpgradePaymentCommand, SubscriptionUpgradeResponseDto>
{
    private const string OperatorWebReturnMode = "OPERATOR_WEB";

    private readonly ISubscriptionUpgradeAttemptRepository _attempts;
    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly ISubscriptionPlanRepository _plans;
    private readonly IOperatorRepository _operators;
    private readonly ISubscriptionPaymentClient _payments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ConfirmSubscriptionUpgradePaymentCommandHandler(
        ISubscriptionUpgradeAttemptRepository attempts,
        IOperatorSubscriptionRepository subscriptions,
        ISubscriptionPlanRepository plans,
        IOperatorRepository operators,
        ISubscriptionPaymentClient payments,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _attempts = attempts;
        _subscriptions = subscriptions;
        _plans = plans;
        _operators = operators;
        _payments = payments;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<SubscriptionUpgradeResponseDto> Handle(
        ConfirmSubscriptionUpgradePaymentCommand request,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var attempt = await _attempts.GetByIdForUpdateAsync(request.UpgradeAttemptId, cancellationToken);
            if (attempt is null || attempt.OperatorId != request.OperatorId)
                throw new NotFoundException(nameof(SubscriptionUpgradeAttempt), request.UpgradeAttemptId);

            var subscription = await _subscriptions.GetByIdForUpdateAsync(attempt.SubscriptionId, cancellationToken);
            if (subscription is null || subscription.OperatorId != request.OperatorId)
                throw new NotFoundException(nameof(OperatorSubscription), attempt.SubscriptionId);

            var targetPlan = await _plans.GetByIdForUpdateAsync(attempt.TargetPlanId, cancellationToken);
            QuoteSubscriptionUpgradeCommandHandler.EnsureTargetVisibleAndActive(
                targetPlan,
                request.OperatorId,
                attempt.TargetPlanId);

            var decisionAt = _clock.UtcNow;
            if (attempt.DueAt <= decisionAt)
            {
                throw new CodedConflictException(
                    "SUBSCRIPTION_UPGRADE_EXPIRED",
                    "The subscription upgrade quote has expired.");
            }

            if (attempt.Status != SubscriptionUpgradeAttemptStatus.INITIATED)
            {
                throw new CodedConflictException(
                    "SUBSCRIPTION_PAYMENT_PENDING",
                    "The subscription upgrade payment has already been started.");
            }

            EnsureQuoteStillMatches(subscription, attempt, decisionAt);
            EnsureQuotaStillFits(subscription, targetPlan!);
            var operatorTenant = await _operators.GetByIdNoTrackingAsync(request.OperatorId, cancellationToken)
                ?? throw new NotFoundException(nameof(Operator), request.OperatorId);

            SubscriptionPaymentCreationResult payment;
            try
            {
                payment = await _payments.CreateAsync(
                    new SubscriptionPaymentCreationRequest(
                        attempt.Id,
                        attempt.SubscriptionId,
                        request.OperatorId,
                        targetPlan!.Id,
                        attempt.BillingPeriod.ToString(),
                        attempt.PaymentMethod.ToString(),
                        attempt.Amount.Amount,
                        CreateSnapshot(attempt, targetPlan, operatorTenant),
                        OperatorWebReturnMode,
                        request.IdempotencyKey,
                        request.ClientIpAddress,
                        attempt.DueAt),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is ICodedHttpException
            {
                StatusCode: 402,
                ErrorCode: "WALLET_INSUFFICIENT_BALANCE",
            })
            {
                throw;
            }

            attempt.BindPendingPayment(payment.PaymentId);
            if (subscription.Status is SubscriptionStatus.ACTIVE or SubscriptionStatus.EXPIRED)
                subscription.MoveToPendingPayment(attempt.PaymentMethod);

            var succeeded = string.Equals(payment.Status, "SUCCEEDED", StringComparison.Ordinal);
            if (succeeded)
            {
                subscription.ActivatePaid(
                    attempt.TargetPlanId,
                    attempt.BillingPeriod,
                    attempt.PaymentMethod,
                    attempt.PeriodFrom,
                    attempt.PeriodTo,
                    attempt.TargetCyclePrice,
                    attempt.IsProrated);
                attempt.MarkSucceeded(payment.PaymentId);
            }

            _attempts.Update(attempt);
            _subscriptions.Update(subscription);
            await _unitOfWork.CommitAsync(cancellationToken);

            var activePlan = succeeded
                ? targetPlan
                : await _plans.GetByIdAsync(subscription.PlanId, cancellationToken);
            return new SubscriptionUpgradeResponseDto(
                subscription.Id,
                attempt.Id,
                succeeded ? SubscriptionStatus.ACTIVE.ToString() : SubscriptionStatus.PENDING_PAYMENT.ToString(),
                payment.PaymentId,
                attempt.Amount.Amount,
                attempt.BillingPeriod.ToString(),
                payment.PaymentRedirectUrl,
                succeeded ? null : attempt.DueAt,
                payment.InvoiceStatus,
                activePlan is null ? null : SubscriptionMapper.ToPlanDto(activePlan),
                succeeded ? null : SubscriptionMapper.ToPlanDto(targetPlan!));
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void EnsureQuoteStillMatches(
        OperatorSubscription subscription,
        SubscriptionUpgradeAttempt attempt,
        DateTimeOffset decisionAt)
    {
        var stale = subscription.PlanId != attempt.SourcePlanId;
        if (attempt.IsProrated)
        {
            stale = stale
                || subscription.BillingPeriod != attempt.BillingPeriod
                || subscription.ExpiresAt != attempt.PeriodTo
                || !SubscriptionEffectiveState.IsEntitlementActive(subscription, decisionAt);
        }

        if (stale)
        {
            throw new CodedConflictException(
                "SUBSCRIPTION_UPGRADE_QUOTE_STALE",
                "The source subscription changed after the quote was created.");
        }
    }

    private static void EnsureQuotaStillFits(
        OperatorSubscription subscription,
        SubscriptionPlan targetPlan)
    {
        if (SubscriptionQuotaPolicy.GetLimitsBelowCurrentUsage(subscription, targetPlan).Count > 0)
        {
            throw new CodedConflictException(
                "SUBSCRIPTION_UPGRADE_QUOTE_STALE",
                "Current usage now exceeds the quoted target plan limits.");
        }
    }

    private static SubscriptionPaymentSnapshot CreateSnapshot(
        SubscriptionUpgradeAttempt attempt,
        SubscriptionPlan targetPlan,
        Operator operatorTenant)
        => new(
            1,
            attempt.SubscriptionId,
            targetPlan.Id,
            targetPlan.Name,
            attempt.BillingPeriod.ToString(),
            attempt.PeriodFrom,
            attempt.PeriodTo,
            new SubscriptionBuyerSnapshot(
                operatorTenant.Name,
                operatorTenant.BusinessRegistrationNumber,
                operatorTenant.TaxCode,
                operatorTenant.ContactEmail,
                operatorTenant.ContactPhone,
                operatorTenant.AddressStreet,
                operatorTenant.AddressWard,
                operatorTenant.AddressProvince));
}
