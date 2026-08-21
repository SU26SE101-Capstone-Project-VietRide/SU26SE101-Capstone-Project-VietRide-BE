using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

internal static class SubscriptionCustomRequestMapper
{
    public static SubscriptionCustomRequestDto ToDto(SubscriptionCustomRequest request)
        => new(
            request.Id,
            request.OperatorId,
            new SubscriptionLimitsDto(
                request.MaxVehicles,
                request.MaxDrivers,
                request.MaxAssistants,
                request.MaxOperatorUsers,
                request.MaxRoutes,
                request.MaxTripsPerMonth),
            new SubscriptionModulesDto(
                request.EnableParcel,
                request.EnableShuttle,
                request.EnableRag),
            request.PreferredBillingPeriod.ToString(),
            request.Note,
            request.Status.ToString(),
            request.ReviewedBy,
            request.ReviewedAt,
            request.RejectionReason,
            request.ApprovedPlanId,
            request.CreatedAt,
            request.UpdatedAt);
}
