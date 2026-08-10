using MediatR;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Subscriptions.RetrySubscriptionPayment;

public sealed class RetrySubscriptionPaymentCommandHandler
    : IRequestHandler<RetrySubscriptionPaymentCommand, SubscriptionUpgradeResponseDto>
{
    private const string OperatorWebReturnMode = "OPERATOR_WEB";

    private readonly ISubscriptionUpgradeAttemptRepository _attempts;
    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly ISubscriptionPlanRepository _plans;
    private readonly IOperatorRepository _operators;
    private readonly ISubscriptionPaymentClient _payments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RetrySubscriptionPaymentCommandHandler(
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
        RetrySubscriptionPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var attempt = await ValidateAndLockAsync(request, cancellationToken);
        var plan = await _plans.GetByIdAsync(attempt.TargetPlanId, cancellationToken)
            ?? throw new NotFoundException(nameof(SubscriptionPlan), attempt.TargetPlanId);
        var operatorTenant = await _operators.GetByIdNoTrackingAsync(request.OperatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Operator), request.OperatorId);
        var snapshot = CreateSnapshot(attempt, plan, operatorTenant);

        var payment = await _payments.CreateAsync(
            new SubscriptionPaymentCreationRequest(
                attempt.Id,
                attempt.SubscriptionId,
                attempt.OperatorId,
                attempt.TargetPlanId,
                attempt.BillingPeriod.ToString(),
                attempt.PaymentMethod.ToString(),
                attempt.Amount.Amount,
                snapshot,
                OperatorWebReturnMode,
                request.IdempotencyKey,
                request.ClientIpAddress,
                attempt.DueAt),
            cancellationToken);

        await BindPaymentAsync(attempt.Id, payment.PaymentId, cancellationToken);
        var subscription = await _subscriptions.GetByIdAsync(attempt.SubscriptionId, cancellationToken)
            ?? throw new NotFoundException(nameof(OperatorSubscription), attempt.SubscriptionId);
        var activePlan = await _plans.GetByIdAsync(subscription.PlanId, cancellationToken)
            ?? throw new NotFoundException(nameof(SubscriptionPlan), subscription.PlanId);
        return new SubscriptionUpgradeResponseDto(
            attempt.SubscriptionId,
            attempt.Id,
            SubscriptionStatus.PENDING_PAYMENT.ToString(),
            payment.PaymentId,
            attempt.Amount.Amount,
            attempt.BillingPeriod.ToString(),
            payment.PaymentRedirectUrl,
            attempt.DueAt,
            payment.InvoiceStatus,
            SubscriptionMapper.ToPlanDto(activePlan),
            SubscriptionMapper.ToPlanDto(plan));
    }

    private async Task<SubscriptionUpgradeAttempt> ValidateAndLockAsync(
        RetrySubscriptionPaymentCommand request,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var attempt = await _attempts.GetByIdForUpdateAsync(request.UpgradeAttemptId, cancellationToken)
                ?? throw new NotFoundException(nameof(SubscriptionUpgradeAttempt), request.UpgradeAttemptId);
            var subscription = await _subscriptions.GetByIdForUpdateAsync(attempt.SubscriptionId, cancellationToken)
                ?? throw new NotFoundException(nameof(OperatorSubscription), attempt.SubscriptionId);

            if (attempt.OperatorId != request.OperatorId || subscription.OperatorId != request.OperatorId)
                throw new ForbiddenException("SUBSCRIPTION_UPGRADE_FORBIDDEN", "Upgrade attempt does not belong to this operator.");
            if (attempt.Status != SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING
                || subscription.Status != SubscriptionStatus.PENDING_PAYMENT)
                throw new CodedConflictException("SUBSCRIPTION_UPGRADE_NOT_PENDING", "Upgrade attempt is no longer pending payment.");
            if (attempt.DueAt <= _clock.UtcNow)
                throw new CodedConflictException("SUBSCRIPTION_UPGRADE_EXPIRED", "Upgrade attempt has expired.");
            if (attempt.LatestPaymentStatus is not (SubscriptionPaymentSessionStatus.FAILED
                or SubscriptionPaymentSessionStatus.EXPIRED))
                throw new CodedConflictException("SUBSCRIPTION_PAYMENT_NOT_RETRYABLE", "Latest payment session is not retryable.");

            await _unitOfWork.CommitAsync(cancellationToken);
            return attempt;
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task BindPaymentAsync(Guid attemptId, Guid paymentId, CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var attempt = await _attempts.GetByIdForUpdateAsync(attemptId, cancellationToken)
                ?? throw new NotFoundException(nameof(SubscriptionUpgradeAttempt), attemptId);
            if (attempt.DueAt <= _clock.UtcNow)
                throw new CodedConflictException("SUBSCRIPTION_UPGRADE_EXPIRED", "Upgrade attempt expired while payment was being created.");
            attempt.BindPendingPayment(paymentId);
            _attempts.Update(attempt);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static SubscriptionPaymentSnapshot CreateSnapshot(
        SubscriptionUpgradeAttempt attempt,
        SubscriptionPlan plan,
        Operator operatorTenant)
    {
        var periodFrom = attempt.CreatedAt;
        var periodTo = attempt.BillingPeriod == SubscriptionBillingPeriod.MONTHLY
            ? periodFrom.AddMonths(1)
            : periodFrom.AddYears(1);
        return new SubscriptionPaymentSnapshot(
            1,
            attempt.SubscriptionId,
            plan.Id,
            plan.Name,
            attempt.BillingPeriod.ToString(),
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
