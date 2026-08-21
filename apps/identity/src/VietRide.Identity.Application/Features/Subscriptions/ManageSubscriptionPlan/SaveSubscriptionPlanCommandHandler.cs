using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Application.Features.Subscriptions.ManageSubscriptionPlan;

public sealed class SaveSubscriptionPlanCommandHandler
    : IRequestHandler<SaveSubscriptionPlanCommand, SubscriptionPlanDto>
{
    private readonly ISubscriptionPlanRepository _plans;
    private readonly IActivityLogRepository _activityLogs;

    public SaveSubscriptionPlanCommandHandler(
        ISubscriptionPlanRepository plans,
        IActivityLogRepository activityLogs)
    {
        _plans = plans;
        _activityLogs = activityLogs;
    }

    public async Task<SubscriptionPlanDto> Handle(
        SaveSubscriptionPlanCommand request,
        CancellationToken cancellationToken)
    {
        var pricePerMonth = Money.FromRaw(request.PricePerMonth);
        var pricePerYear = Money.FromRaw(request.PricePerYear);
        SubscriptionPlan plan;

        if (request.PlanId.HasValue)
        {
            plan = await _plans.GetByIdForUpdateAsync(request.PlanId.Value, cancellationToken)
                ?? throw new NotFoundException(nameof(SubscriptionPlan), request.PlanId.Value);

            if (plan.Id == SubscriptionPlan.StarterPlanId && !request.IsActive)
                throw new CodedConflictException("STARTER_PLAN_REQUIRED", "The Starter plan cannot be deactivated.");

            try
            {
                plan.Update(
                    request.Name, request.Description, pricePerMonth, pricePerYear,
                    request.MaxVehicles, request.MaxDrivers, request.MaxAssistants,
                    request.MaxOperatorUsers, request.MaxRoutes, request.MaxTripsPerMonth,
                    request.EnableParcel, request.EnableShuttle, request.EnableRag, request.IsActive);
            }
            catch (InvalidOperationException exception) when (plan.PlanType == SubscriptionPlanType.CUSTOM)
            {
                throw new CodedConflictException("CUSTOM_PLAN_IMMUTABLE", exception.Message);
            }
            _plans.Update(plan);
            if (plan.PlanType == SubscriptionPlanType.CUSTOM && !plan.IsActive && request.CallerUserId.HasValue)
            {
                await _activityLogs.AddAsync(
                    ActivityLog.Create(
                        request.CallerUserId.Value,
                        ActivityLogAction.DEACTIVATE_CUSTOM_SUBSCRIPTION_PLAN,
                        JsonSerializer.Serialize(new
                        {
                            planId = plan.Id,
                            operatorId = plan.OwnerOperatorId,
                        })),
                    cancellationToken);
            }
        }
        else
        {
            plan = SubscriptionPlan.Create(
                request.Name, request.Description, pricePerMonth, pricePerYear,
                request.MaxVehicles, request.MaxDrivers, request.MaxAssistants,
                request.MaxOperatorUsers, request.MaxRoutes, request.MaxTripsPerMonth,
                request.EnableParcel, request.EnableShuttle, request.EnableRag);
            if (!request.IsActive)
                plan.Deactivate();
            await _plans.AddAsync(plan, cancellationToken);
        }

        return SubscriptionMapper.ToPlanDto(plan);
    }
}
