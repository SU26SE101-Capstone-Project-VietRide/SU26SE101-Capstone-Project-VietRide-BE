using MediatR;
using VietRide.Payment.Application.Models;

namespace VietRide.Payment.Application.Features.Internal.Payments.CreateSubscriptionPayment;

public sealed record CreateSubscriptionPaymentCommand(
    Guid UpgradeAttemptId,
    Guid SubscriptionId,
    Guid OperatorId,
    Guid PlanId,
    string BillingPeriod,
    string PaymentMethod,
    long Amount,
    SubscriptionPaymentContextV1 Context,
    string IdempotencyKey,
    string ClientIpAddress,
    DateTimeOffset? DueAt = null,
    string? ReturnMode = null) : IRequest<CreateSubscriptionPaymentResult>;
