using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Application.Features.Internal.Payments.GetPaymentContextReadiness;

public sealed record GetPaymentContextReadinessQuery : IRequest<PaymentContextReadinessResult>;

public sealed record PaymentContextReadinessResult(
    bool ReadyForPhaseB,
    int PendingRedirectWithoutContext,
    int SucceededWithoutContext,
    int Quarantined);

public sealed class GetPaymentContextReadinessQueryHandler
    : IRequestHandler<GetPaymentContextReadinessQuery, PaymentContextReadinessResult>
{
    private readonly IPaymentRepository _payments;

    public GetPaymentContextReadinessQueryHandler(IPaymentRepository payments)
    {
        _payments = payments;
    }

    public async Task<PaymentContextReadinessResult> Handle(
        GetPaymentContextReadinessQuery request,
        CancellationToken cancellationToken)
    {
        var query = _payments.QueryNoTracking();
        var pending = await query.CountAsync(
            payment => payment.Status == PaymentStatus.PENDING_REDIRECT && payment.Context == "{}",
            cancellationToken);
        var succeeded = await query.CountAsync(
            payment => payment.Status == PaymentStatus.SUCCEEDED
                && payment.Context == "{}"
                && !payment.ContextReconciliationRequired,
            cancellationToken);
        var quarantined = await query.CountAsync(
            payment => payment.ContextReconciliationRequired,
            cancellationToken);

        return new PaymentContextReadinessResult(
            pending == 0 && succeeded == 0,
            pending,
            succeeded,
            quarantined);
    }
}
