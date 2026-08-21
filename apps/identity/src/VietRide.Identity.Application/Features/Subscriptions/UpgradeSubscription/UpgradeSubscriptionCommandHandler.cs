using MediatR;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Subscriptions.ConfirmSubscriptionUpgradePayment;
using VietRide.Identity.Application.Features.Subscriptions.QuoteSubscriptionUpgrade;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Subscriptions.UpgradeSubscription;

public sealed class UpgradeSubscriptionCommandHandler
    : IRequestHandler<UpgradeSubscriptionCommand, SubscriptionUpgradeResponseDto>
{
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
        var billingPeriod = SubscriptionMapper.ParseBillingPeriod(request.BillingPeriod);
        var paymentMethod = Enum.Parse<SubscriptionPaymentMethod>(request.PaymentMethod, ignoreCase: false);
        var attempt = await GetOrCreateAttemptAsync(
            request,
            billingPeriod,
            paymentMethod,
            cancellationToken);
        var confirmHandler = new ConfirmSubscriptionUpgradePaymentCommandHandler(
            _attempts,
            _subscriptions,
            _plans,
            _operators,
            _payments,
            _unitOfWork,
            _clock);
        return await confirmHandler.Handle(
            new ConfirmSubscriptionUpgradePaymentCommand(
                request.OperatorId,
                attempt.Id,
                request.IdempotencyKey,
                request.ClientIpAddress),
            cancellationToken);
    }

    private async Task<SubscriptionUpgradeAttempt> GetOrCreateAttemptAsync(
        UpgradeSubscriptionCommand request,
        SubscriptionBillingPeriod billingPeriod,
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
            if (activeAttempt is not null)
            {
                if (CanRetryWalletAttempt(activeAttempt, request))
                {
                    await _unitOfWork.CommitAsync(cancellationToken);
                    return activeAttempt;
                }

                throw new CodedConflictException(
                    "SUBSCRIPTION_PAYMENT_PENDING",
                    "An upgrade payment is already pending.");
            }

            if (subscription.Status == SubscriptionStatus.PENDING_PAYMENT)
            {
                throw new CodedConflictException(
                    "SUBSCRIPTION_PAYMENT_PENDING",
                    "An upgrade payment is already pending.");
            }

            var decisionAt = _clock.UtcNow;
            var effectiveStatus = SubscriptionEffectiveState.GetStatus(subscription, decisionAt);
            if (effectiveStatus is not (SubscriptionStatus.ACTIVE or SubscriptionStatus.EXPIRED))
            {
                throw new CodedConflictException(
                    "SUBSCRIPTION_NOT_UPGRADABLE",
                    "Only active or expired subscriptions can start an upgrade.");
            }

            var targetPlan = await _plans.GetByIdForUpdateAsync(request.PlanId, cancellationToken);
            QuoteSubscriptionUpgradeCommandHandler.EnsureTargetVisibleAndActive(
                targetPlan,
                request.OperatorId,
                request.PlanId);
            QuoteSubscriptionUpgradeCommandHandler.EnsureQuotaFloor(subscription, targetPlan!);
            var price = SubscriptionUpgradePricing.Calculate(
                subscription,
                targetPlan!,
                billingPeriod,
                decisionAt);
            var attempt = SubscriptionUpgradeAttempt.CreateQuote(
                subscription.Id,
                request.OperatorId,
                subscription.PlanId,
                request.PlanId,
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
            return attempt;
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

    private static bool CanRetryWalletAttempt(
        SubscriptionUpgradeAttempt attempt,
        UpgradeSubscriptionCommand request)
        => attempt.Status == SubscriptionUpgradeAttemptStatus.INITIATED
            && attempt.PaymentMethod == SubscriptionPaymentMethod.WALLET
            && attempt.PaymentId is null
            && attempt.LatestPaymentStatus == SubscriptionPaymentSessionStatus.NONE
            && attempt.OperatorId == request.OperatorId
            && attempt.TargetPlanId == request.PlanId
            && string.Equals(attempt.BillingPeriod.ToString(), request.BillingPeriod, StringComparison.Ordinal)
            && string.Equals(attempt.PaymentMethod.ToString(), request.PaymentMethod, StringComparison.Ordinal);

}
