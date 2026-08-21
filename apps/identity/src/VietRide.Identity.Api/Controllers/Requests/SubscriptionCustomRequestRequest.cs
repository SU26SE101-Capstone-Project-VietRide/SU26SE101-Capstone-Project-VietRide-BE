using VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

namespace VietRide.Identity.Api.Controllers.Requests;

public sealed record SubscriptionCustomRequestRequest(
    int MaxVehicles,
    int MaxDrivers,
    int MaxAssistants,
    int MaxOperatorUsers,
    int MaxRoutes,
    int MaxTripsPerMonth,
    bool EnableParcel,
    bool EnableShuttle,
    bool EnableRag,
    string PreferredBillingPeriod,
    string? Note)
{
    public CreateSubscriptionCustomRequestCommand ToCommand(Guid callerUserId, Guid operatorId)
        => new(
            callerUserId,
            operatorId,
            MaxVehicles,
            MaxDrivers,
            MaxAssistants,
            MaxOperatorUsers,
            MaxRoutes,
            MaxTripsPerMonth,
            EnableParcel,
            EnableShuttle,
            EnableRag,
            PreferredBillingPeriod,
            Note);
}
