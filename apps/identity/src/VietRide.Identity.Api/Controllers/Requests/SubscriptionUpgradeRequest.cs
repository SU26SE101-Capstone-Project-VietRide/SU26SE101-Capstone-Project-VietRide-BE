namespace VietRide.Identity.Api.Controllers.Requests;

public sealed record SubscriptionUpgradeRequest(
    Guid PlanId,
    string BillingPeriod,
    string PaymentMethod);
