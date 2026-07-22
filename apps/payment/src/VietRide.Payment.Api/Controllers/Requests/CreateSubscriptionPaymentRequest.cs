using VietRide.Payment.Application.Features.Internal.Payments.CreateSubscriptionPayment;
using VietRide.Payment.Application.Models;

namespace VietRide.Payment.Api.Controllers.Requests;

public sealed record CreateSubscriptionPaymentRequest(
    Guid UpgradeAttemptId,
    Guid SubscriptionId,
    Guid OperatorId,
    Guid PlanId,
    string BillingPeriod,
    string PaymentMethod,
    long Amount,
    DateTimeOffset DueAt,
    SubscriptionPaymentContextV1 Context)
{
    public CreateSubscriptionPaymentCommand ToCommand(string idempotencyKey, string clientIpAddress)
        => new(
            UpgradeAttemptId,
            SubscriptionId,
            OperatorId,
            PlanId,
            BillingPeriod,
            PaymentMethod,
            Amount,
            Context,
            idempotencyKey,
            clientIpAddress,
            DueAt);
}
