using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Identity.Application.Features.Subscriptions.CancelSubscriptionUpgrade;

[SkipTransaction]
public sealed record CancelSubscriptionUpgradeCommand(Guid OperatorId, Guid UpgradeAttemptId)
    : IRequest<CancelSubscriptionUpgradeResponseDto>;
