using MediatR;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed record ApproveSubscriptionCustomRequestCommand(
    Guid CallerUserId,
    Guid RequestId,
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
    bool EnableRag) : IRequest<SubscriptionCustomRequestDto>;
