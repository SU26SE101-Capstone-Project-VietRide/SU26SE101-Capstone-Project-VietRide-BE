using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Domain.Exceptions;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Internal.Operators.QuotaAllocations;

public sealed class ClaimQuotaAllocationCommandHandler : IRequestHandler<ClaimQuotaAllocationCommand, QuotaAllocationDto>
{
    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly ISubscriptionQuotaAllocationRepository _allocations;
    public ClaimQuotaAllocationCommandHandler(IOperatorSubscriptionRepository subscriptions, ISubscriptionQuotaAllocationRepository allocations) => (_subscriptions, _allocations) = (subscriptions, allocations);
    public async Task<QuotaAllocationDto> Handle(ClaimQuotaAllocationCommand request, CancellationToken ct)
    {
        if (!Enum.TryParse<SubscriptionUsageResource>(request.Resource, false, out var resource)) throw new CodedValidationException("VALIDATION_ERROR", "Invalid quota resource.");
        await _allocations.AcquireLockAsync(request.OperatorId, resource, request.ResourceId, ct);
        var existing = await _allocations.GetActiveAsync(request.OperatorId, resource, request.ResourceId, ct);
        if (existing is not null) return new(existing.Id, existing.Resource.ToString(), existing.ResourceId, existing.PeriodKey);
        var current = await _subscriptions.GetCurrentWithPlanByOperatorIdAsync(request.OperatorId, ct) ?? throw new NotFoundException(nameof(OperatorSubscription), request.OperatorId);
        if (current.Subscription.Status == SubscriptionStatus.EXPIRED) throw new IdentityDomainException("SUBSCRIPTION_EXPIRED", "Operator subscription has expired.");
        if (current.Subscription.Status == SubscriptionStatus.PENDING_PAYMENT) throw new CodedConflictException("SUBSCRIPTION_PAYMENT_PENDING", "Subscription payment is pending.");
        if (current.Subscription.Status != SubscriptionStatus.ACTIVE) throw new CodedValidationException("VALIDATION_ERROR", "Subscription must be ACTIVE.");
        var updated = await _subscriptions.TryIncrementUsageWithinLimitAsync(request.OperatorId, resource, 1, ct);
        if (updated is null) throw new IdentityDomainException("SUBSCRIPTION_LIMIT_EXCEEDED", "Subscription limit exceeded.");
        var allocation = SubscriptionQuotaAllocation.Create(request.OperatorId, current.Subscription.Id, resource, request.ResourceId, request.PeriodKey);
        await _allocations.AddAsync(allocation, ct);
        return new(allocation.Id, allocation.Resource.ToString(), allocation.ResourceId, allocation.PeriodKey);
    }
}

public sealed class ReleaseQuotaAllocationCommandHandler : IRequestHandler<ReleaseQuotaAllocationCommand, Unit>
{
    private readonly ISubscriptionQuotaAllocationRepository _allocations;
    private readonly IOperatorSubscriptionRepository _subscriptions;
    public ReleaseQuotaAllocationCommandHandler(ISubscriptionQuotaAllocationRepository allocations, IOperatorSubscriptionRepository subscriptions) => (_allocations, _subscriptions) = (allocations, subscriptions);
    public async Task<Unit> Handle(ReleaseQuotaAllocationCommand request, CancellationToken ct)
    {
        var allocation = await _allocations.GetByIdAsync(request.AllocationId, ct);
        if (allocation is null || allocation.OperatorId != request.OperatorId || allocation.ReleasedAt.HasValue) return Unit.Value;
        await _subscriptions.TryDecrementUsageAsync(request.OperatorId, allocation.Resource, ct);
        allocation.Release(DateTimeOffset.UtcNow);
        _allocations.Update(allocation);
        return Unit.Value;
    }
}
