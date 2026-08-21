using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Identity.Application.Features.Subscriptions.ConfirmSubscriptionUpgradePayment;

[SkipTransaction]
public sealed record ConfirmSubscriptionUpgradePaymentCommand(
    Guid OperatorId,
    Guid UpgradeAttemptId,
    string IdempotencyKey,
    string ClientIpAddress) : IRequest<SubscriptionUpgradeResponseDto>;
