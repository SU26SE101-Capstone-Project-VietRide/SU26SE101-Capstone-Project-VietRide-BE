using MediatR;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;

namespace VietRide.Identity.Application.Features.Subscriptions.CancelSubscriptionUpgrade;

public sealed class CancelSubscriptionUpgradeCommandHandler
    : IRequestHandler<CancelSubscriptionUpgradeCommand, CancelSubscriptionUpgradeResponseDto>
{
    private static readonly HashSet<string> TerminalPaymentStatuses = new(StringComparer.Ordinal)
    {
        "FAILED",
        "EXPIRED",
        "REFUNDED",
    };

    private readonly ISubscriptionUpgradeAttemptRepository _attempts;
    private readonly ISubscriptionPaymentClient _payments;
    private readonly IUnitOfWork _unitOfWork;

    public CancelSubscriptionUpgradeCommandHandler(
        ISubscriptionUpgradeAttemptRepository attempts,
        ISubscriptionPaymentClient payments,
        IUnitOfWork unitOfWork)
    {
        _attempts = attempts;
        _payments = payments;
        _unitOfWork = unitOfWork;
    }

    public async Task<CancelSubscriptionUpgradeResponseDto> Handle(
        CancelSubscriptionUpgradeCommand request,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var attempt = await _attempts.GetByIdForUpdateAsync(request.UpgradeAttemptId, cancellationToken);
            if (attempt is null || attempt.OperatorId != request.OperatorId)
                throw new NotFoundException(nameof(SubscriptionUpgradeAttempt), request.UpgradeAttemptId);

            if (attempt.Status == SubscriptionUpgradeAttemptStatus.CANCELLED)
            {
                await _unitOfWork.CommitAsync(cancellationToken);
                return ToDto(attempt);
            }

            if (attempt.Status != SubscriptionUpgradeAttemptStatus.INITIATED)
                throw NotCancellable(attempt.Status == SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING);
            if (attempt.PaymentId.HasValue)
                throw NotCancellable(paymentStarted: true);

            var paymentStatuses = await _payments.GetStatusesAsync([attempt.Id], cancellationToken);
            if (paymentStatuses.Any(payment => !TerminalPaymentStatuses.Contains(payment.Status)))
                throw NotCancellable(paymentStarted: true);

            attempt.Cancel();
            _attempts.Update(attempt);
            await _unitOfWork.CommitAsync(cancellationToken);
            return ToDto(attempt);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static CancelSubscriptionUpgradeResponseDto ToDto(SubscriptionUpgradeAttempt attempt)
        => new(attempt.Id, attempt.Status.ToString());

    private static CodedConflictException NotCancellable(bool paymentStarted)
        => paymentStarted
            ? new CodedConflictException(
                "SUBSCRIPTION_UPGRADE_PAYMENT_ALREADY_STARTED",
                "The subscription upgrade payment has already started and cannot be cancelled.")
            : new CodedConflictException(
                "SUBSCRIPTION_UPGRADE_NOT_CANCELLABLE",
                "The subscription upgrade quote can no longer be cancelled.");
}
