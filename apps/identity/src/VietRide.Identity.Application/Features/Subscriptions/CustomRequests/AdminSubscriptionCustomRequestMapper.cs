using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

internal static class AdminSubscriptionCustomRequestMapper
{
    public static AdminSubscriptionCustomRequestDto ToDto(
        SubscriptionCustomRequest request,
        string operatorName)
        => new(
            request.Id,
            request.OperatorId,
            operatorName,
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
