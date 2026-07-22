using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Identity.Application.Features.Subscriptions.RetrySubscriptionPayment;

[SkipTransaction]
public sealed record RetrySubscriptionPaymentCommand(
    Guid OperatorId,
    Guid UpgradeAttemptId,
    string IdempotencyKey,
    string ClientIpAddress) : IRequest<SubscriptionUpgradeResponseDto>;
