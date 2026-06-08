namespace VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;

public sealed record InternalSubscriptionUsageDto(
    int CurrentVehicles,
    int CurrentDrivers,
    int CurrentAssistants,
    int CurrentOperatorUsers,
    int CurrentRoutes,
    int CurrentTripsThisMonth);
