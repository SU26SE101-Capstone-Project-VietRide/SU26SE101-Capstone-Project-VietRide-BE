using VietRide.Identity.Application.Features.Subscriptions.ManageSubscriptionPlan;

namespace VietRide.Identity.Api.Controllers.Requests;

public sealed record SubscriptionPlanRequest(
    string Name,
    string? Description,
    long PricePerMonth,
    long PricePerYear,
    int MaxVehicles,
    int MaxDrivers,
    int MaxAssistants,
    int MaxOperatorUsers,
    int MaxRoutes,
    int MaxTripsPerMonth,
    bool EnableParcel,
    bool EnableShuttle,
    bool EnableRag,
    bool IsActive = true)
{
    public SaveSubscriptionPlanCommand ToCommand(Guid? planId, Guid? callerUserId = null)
        => new(
            planId,
            Name,
            Description,
            PricePerMonth,
            PricePerYear,
            MaxVehicles,
            MaxDrivers,
            MaxAssistants,
            MaxOperatorUsers,
            MaxRoutes,
            MaxTripsPerMonth,
            EnableParcel,
            EnableShuttle,
            EnableRag,
            IsActive,
            callerUserId);
}
