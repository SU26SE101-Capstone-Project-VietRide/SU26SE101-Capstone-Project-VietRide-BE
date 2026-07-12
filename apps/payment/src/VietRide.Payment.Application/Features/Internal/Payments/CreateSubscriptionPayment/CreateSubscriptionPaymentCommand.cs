using MediatR;

namespace VietRide.Payment.Application.Features.Internal.Payments.CreateSubscriptionPayment;

public sealed record CreateSubscriptionPaymentCommand(
    Guid UpgradeAttemptId,
    Guid SubscriptionId,
    Guid OperatorId,
    Guid PlanId,
    string BillingPeriod,
    long Amount,
    string IdempotencyKey,
    string ClientIpAddress) : IRequest<CreateSubscriptionPaymentResult>;
