using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Application.Features.Subscriptions.ManageSubscriptionPlan;

public sealed class SaveSubscriptionPlanCommandHandler
    : IRequestHandler<SaveSubscriptionPlanCommand, SubscriptionPlanDto>
{
    private readonly ISubscriptionPlanRepository _plans;

    public SaveSubscriptionPlanCommandHandler(ISubscriptionPlanRepository plans)
    {
        _plans = plans;
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
            plan = await _plans.GetByIdAsync(request.PlanId.Value, cancellationToken)
                ?? throw new NotFoundException(nameof(SubscriptionPlan), request.PlanId.Value);

            if (plan.Id == SubscriptionPlan.StarterPlanId && !request.IsActive)
                throw new CodedConflictException("STARTER_PLAN_REQUIRED", "The Starter plan cannot be deactivated.");

            plan.Update(
                request.Name, request.Description, pricePerMonth, pricePerYear,
                request.MaxVehicles, request.MaxDrivers, request.MaxAssistants,
                request.MaxOperatorUsers, request.MaxRoutes, request.MaxTripsPerMonth,
                request.EnableParcel, request.EnableShuttle, request.EnableRag, request.IsActive);
            _plans.Update(plan);
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
