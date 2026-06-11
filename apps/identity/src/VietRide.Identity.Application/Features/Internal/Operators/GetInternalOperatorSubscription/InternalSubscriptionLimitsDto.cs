namespace VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;

public sealed record InternalSubscriptionLimitsDto(
    int MaxVehicles,
    int MaxDrivers,
    int MaxAssistants,
    int MaxOperatorUsers,
    int MaxRoutes,
    int MaxTripsPerMonth);
