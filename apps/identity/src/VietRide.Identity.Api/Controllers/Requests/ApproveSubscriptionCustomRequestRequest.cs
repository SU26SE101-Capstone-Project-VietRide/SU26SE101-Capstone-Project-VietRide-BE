using VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

namespace VietRide.Identity.Api.Controllers.Requests;

public sealed record ApproveSubscriptionCustomRequestRequest(
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
    bool EnableRag)
{
    public ApproveSubscriptionCustomRequestCommand ToCommand(Guid callerUserId, Guid requestId)
        => new(
            callerUserId,
            requestId,
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
            EnableRag);
}
