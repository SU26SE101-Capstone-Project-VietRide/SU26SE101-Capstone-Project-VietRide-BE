using MediatR;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed record CreateSubscriptionCustomRequestCommand(
    Guid CallerUserId,
    Guid OperatorId,
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
    string? Note) : IRequest<SubscriptionCustomRequestDto>;
