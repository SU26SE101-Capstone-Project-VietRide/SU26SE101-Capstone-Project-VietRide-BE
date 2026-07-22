using MediatR;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Models;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Features.Internal.Payments.GetSubscriptionPaymentStatuses;

public sealed class GetSubscriptionPaymentStatusesQueryHandler
    : IRequestHandler<GetSubscriptionPaymentStatusesQuery, IReadOnlyList<SubscriptionPaymentStatusDto>>
{
    private readonly IPaymentRepository _payments;

    public GetSubscriptionPaymentStatusesQueryHandler(IPaymentRepository payments)
    {
        _payments = payments;
    }

    public async Task<IReadOnlyList<SubscriptionPaymentStatusDto>> Handle(
        GetSubscriptionPaymentStatusesQuery request,
        CancellationToken cancellationToken)
    {
        if (request.UpgradeAttemptIds.Count is < 1 or > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "Between 1 and 100 upgrade attempt ids are required.");

        var payments = await _payments.ListLatestSubscriptionPaymentsAsync(
            request.UpgradeAttemptIds.Distinct().ToArray(),
            cancellationToken).ConfigureAwait(false);
        return payments.Select(payment =>
        {
            var context = SubscriptionPaymentContextCodec.DeserializeTrusted(payment.Context);
            return new SubscriptionPaymentStatusDto(
                payment.Id,
                payment.ReferenceId,
                payment.OperatorId ?? Guid.Empty,
                context.OperatorSubscriptionId,
                context.PlanId,
                payment.Status.ToString(),
                payment.Amount.Amount,
                payment.Method.ToString(),
                context.BillingPeriod,
                context.PeriodFrom,
                context.PeriodTo,
                payment.SucceededAt,
                payment.DueAt);
        }).ToArray();
    }
}
