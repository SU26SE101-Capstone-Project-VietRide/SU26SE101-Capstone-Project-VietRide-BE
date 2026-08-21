using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Subscriptions.QuoteSubscriptionUpgrade;

public sealed class QuoteSubscriptionUpgradeCommandHandler
    : IRequestHandler<QuoteSubscriptionUpgradeCommand, SubscriptionUpgradeQuoteDto>
{
    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly ISubscriptionPlanRepository _plans;
    private readonly ISubscriptionUpgradeAttemptRepository _attempts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public QuoteSubscriptionUpgradeCommandHandler(
        IOperatorSubscriptionRepository subscriptions,
        ISubscriptionPlanRepository plans,
        ISubscriptionUpgradeAttemptRepository attempts,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _subscriptions = subscriptions;
        _plans = plans;
        _attempts = attempts;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<SubscriptionUpgradeQuoteDto> Handle(
        QuoteSubscriptionUpgradeCommand request,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var subscription = await _subscriptions.GetCurrentByOperatorIdForUpdateAsync(
                request.OperatorId,
                cancellationToken)
                ?? throw new NotFoundException(nameof(OperatorSubscription), request.OperatorId);
            var decisionAt = _clock.UtcNow;

            var replay = await _attempts.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
            if (replay is not null)
            {
                EnsureReplayMatches(replay, request);
                await _unitOfWork.CommitAsync(cancellationToken);
                return ToDto(replay);
            }

            if (await _attempts.GetActiveBySubscriptionIdAsync(subscription.Id, cancellationToken) is not null
                || subscription.Status == SubscriptionStatus.PENDING_PAYMENT)
            {
                throw new CodedConflictException(
                    "SUBSCRIPTION_UPGRADE_ALREADY_ACTIVE",
                    "An active subscription upgrade already exists.");
            }

            var effectiveStatus = SubscriptionEffectiveState.GetStatus(subscription, decisionAt);
            if (effectiveStatus is not (SubscriptionStatus.ACTIVE or SubscriptionStatus.EXPIRED))
            {
                throw new CodedConflictException(
                    "SUBSCRIPTION_NOT_UPGRADABLE",
                    "Only active or expired subscriptions can start an upgrade.");
            }

            var targetPlan = await _plans.GetByIdForUpdateAsync(request.PlanId, cancellationToken);
            EnsureTargetVisibleAndActive(targetPlan, request.OperatorId, request.PlanId);
            EnsureQuotaFloor(subscription, targetPlan!);

            var billingPeriod = SubscriptionMapper.ParseBillingPeriod(request.BillingPeriod);
            var paymentMethod = Enum.Parse<SubscriptionPaymentMethod>(request.PaymentMethod, ignoreCase: false);
            var price = SubscriptionUpgradePricing.Calculate(subscription, targetPlan!, billingPeriod, decisionAt);
            var attempt = SubscriptionUpgradeAttempt.CreateQuote(
                subscription.Id,
                request.OperatorId,
                subscription.PlanId,
                targetPlan!.Id,
                billingPeriod,
                price.AmountDue,
                paymentMethod,
                request.IdempotencyKey,
                SubscriptionFallbackPolicy.RESTORE_CURRENT,
                decisionAt,
                price.DueAt,
                price.PeriodFrom,
                price.PeriodTo,
                price.CurrentCyclePrice,
                price.TargetCyclePrice,
                price.UnusedCredit,
                price.ProratedTargetAmount,
                price.IsProrated);
            await _attempts.AddAsync(attempt, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
            return ToDto(attempt);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static void EnsureTargetVisibleAndActive(
        SubscriptionPlan? targetPlan,
        Guid operatorId,
        Guid requestedPlanId)
    {
        if (targetPlan is null || !targetPlan.IsVisibleTo(operatorId))
            throw new NotFoundException(nameof(SubscriptionPlan), requestedPlanId);
        if (!targetPlan.IsActive)
        {
            throw new CodedConflictException(
                "SUBSCRIPTION_UPGRADE_TARGET_PLAN_INACTIVE",
                "The selected target plan is inactive.");
        }
    }

    internal static void EnsureQuotaFloor(OperatorSubscription subscription, SubscriptionPlan targetPlan)
    {
        var violations = SubscriptionQuotaPolicy.GetLimitsBelowCurrentUsage(subscription, targetPlan);
        if (violations.Count > 0)
        {
            throw new CodedValidationException(
                "SUBSCRIPTION_UPGRADE_TARGET_LIMIT_BELOW_USAGE",
                $"Target plan limits are below current usage: {string.Join("; ", violations.Select(violation => $"{violation.Field}: granted {violation.GrantedLimit}, current usage {violation.CurrentUsage}"))}");
        }
    }

    private static void EnsureReplayMatches(
        SubscriptionUpgradeAttempt attempt,
        QuoteSubscriptionUpgradeCommand request)
    {
        if (attempt.OperatorId != request.OperatorId
            || attempt.TargetPlanId != request.PlanId
            || !string.Equals(attempt.BillingPeriod.ToString(), request.BillingPeriod, StringComparison.Ordinal)
            || !string.Equals(attempt.PaymentMethod.ToString(), request.PaymentMethod, StringComparison.Ordinal))
        {
            throw new CodedValidationException(
                "IDEMPOTENCY_KEY_MISMATCH",
                "Idempotency-Key was already used with a different subscription upgrade quote.");
        }
    }

    internal static SubscriptionUpgradeQuoteDto ToDto(SubscriptionUpgradeAttempt attempt)
        => new(
            attempt.Id,
            attempt.SourcePlanId,
            attempt.TargetPlanId,
            attempt.BillingPeriod.ToString(),
            attempt.PaymentMethod.ToString(),
            attempt.IsProrated,
            attempt.CurrentCyclePrice.Amount,
            attempt.TargetCyclePrice.Amount,
            attempt.UnusedCredit.Amount,
            attempt.ProratedTargetAmount.Amount,
            attempt.Amount.Amount,
            attempt.PeriodFrom,
            attempt.PeriodTo,
            attempt.QuotedAt,
            attempt.DueAt,
            "VND",
            attempt.Status.ToString());
}
