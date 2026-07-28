using System.Globalization;
using System.Text.Json;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Internal.Operators.IncrementOperatorUsage;

public sealed class SubscriptionUsageWarningPublisher : ISubscriptionUsageWarningPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISubscriptionUsageWarningMarkerRepository _markers;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public SubscriptionUsageWarningPublisher(
        ISubscriptionUsageWarningMarkerRepository markers,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _markers = markers;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task EnqueueIfThresholdCrossedAsync(
        OperatorSubscription subscription,
        SubscriptionPlan plan,
        SubscriptionUsageResource resource,
        int delta,
        string? periodKey,
        CancellationToken cancellationToken)
    {
        var used = GetUsage(subscription, resource);
        var limit = GetLimit(plan, resource);
        if (limit <= 0)
            return;

        var before = used - delta;
        if ((long)before * 100 >= (long)limit * 80
            || (long)used * 100 < (long)limit * 80)
            return;

        var now = _clock.UtcNow;
        var canonicalPeriodKey = resource == SubscriptionUsageResource.TRIPS_THIS_MONTH
            ? periodKey ?? now.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            : subscription.Id.ToString("D");
        if (await _markers.ExistsAsync(
                subscription.Id,
                resource,
                canonicalPeriodKey,
                cancellationToken))
            return;

        var eventId = Guid.NewGuid();
        await _markers.AddAsync(
            SubscriptionUsageWarningMarker.Create(
                eventId,
                subscription.Id,
                resource,
                canonicalPeriodKey),
            cancellationToken);
        var warning = new SubscriptionUsageWarningIntegrationEvent(
            eventId,
            now,
            subscription.Id,
            subscription.OperatorId,
            resource.ToString(),
            canonicalPeriodKey,
            used,
            limit,
            decimal.Round(used * 100m / limit, 2, MidpointRounding.AwayFromZero));
        await _outbox.EnqueueAsync(
            warning.EventId,
            warning.EventType,
            JsonSerializer.Serialize(warning, JsonOptions),
            cancellationToken);
    }

    private static int GetUsage(OperatorSubscription subscription, SubscriptionUsageResource resource)
        => resource switch
        {
            SubscriptionUsageResource.VEHICLES => subscription.CurrentVehicles,
            SubscriptionUsageResource.DRIVERS => subscription.CurrentDrivers,
            SubscriptionUsageResource.ASSISTANTS => subscription.CurrentAssistants,
            SubscriptionUsageResource.OPERATOR_USERS => subscription.CurrentOperatorUsers,
            SubscriptionUsageResource.ROUTES => subscription.CurrentRoutes,
            SubscriptionUsageResource.TRIPS_THIS_MONTH => subscription.CurrentTripsThisMonth,
            _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null),
        };

    private static int GetLimit(SubscriptionPlan plan, SubscriptionUsageResource resource)
        => resource switch
        {
            SubscriptionUsageResource.VEHICLES => plan.MaxVehicles,
            SubscriptionUsageResource.DRIVERS => plan.MaxDrivers,
            SubscriptionUsageResource.ASSISTANTS => plan.MaxAssistants,
            SubscriptionUsageResource.OPERATOR_USERS => plan.MaxOperatorUsers,
            SubscriptionUsageResource.ROUTES => plan.MaxRoutes,
            SubscriptionUsageResource.TRIPS_THIS_MONTH => plan.MaxTripsPerMonth,
            _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null),
        };
}
