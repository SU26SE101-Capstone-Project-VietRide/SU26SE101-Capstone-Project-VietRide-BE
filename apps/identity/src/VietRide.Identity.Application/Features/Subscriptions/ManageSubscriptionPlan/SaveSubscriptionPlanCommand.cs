using MediatR;

namespace VietRide.Identity.Application.Features.Subscriptions.ManageSubscriptionPlan;

public sealed record SaveSubscriptionPlanCommand(
    Guid? PlanId,
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
    bool IsActive,
    Guid? CallerUserId = null) : IRequest<SubscriptionPlanDto>;
