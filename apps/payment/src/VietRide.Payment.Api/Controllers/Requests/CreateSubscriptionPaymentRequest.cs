using VietRide.Payment.Application.Features.Internal.Payments.CreateSubscriptionPayment;

namespace VietRide.Payment.Api.Controllers.Requests;

public sealed record CreateSubscriptionPaymentRequest(
    Guid UpgradeAttemptId,
    Guid SubscriptionId,
    Guid OperatorId,
    Guid PlanId,
    string BillingPeriod,
    long Amount)
{
    public CreateSubscriptionPaymentCommand ToCommand(string idempotencyKey, string clientIpAddress)
        => new(UpgradeAttemptId, SubscriptionId, OperatorId, PlanId, BillingPeriod, Amount, idempotencyKey, clientIpAddress);
}
