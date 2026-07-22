using MediatR;

namespace VietRide.Payment.Application.Features.Internal.Payments.GetSubscriptionPaymentStatuses;

public sealed record GetSubscriptionPaymentStatusesQuery(IReadOnlyCollection<Guid> UpgradeAttemptIds)
    : IRequest<IReadOnlyList<SubscriptionPaymentStatusDto>>;
