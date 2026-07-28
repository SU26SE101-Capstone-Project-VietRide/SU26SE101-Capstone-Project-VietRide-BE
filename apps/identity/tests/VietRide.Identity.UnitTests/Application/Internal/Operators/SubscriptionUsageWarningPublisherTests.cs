using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Internal.Operators.IncrementOperatorUsage;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.UnitTests.Application.Internal.Operators;

public sealed class SubscriptionUsageWarningPublisherTests
{
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CrossingEightyPercent_PersistsMarkerAndCanonicalOutboxWithSameIdentity()
    {
        var subscription = CreateSubscription(currentDrivers: 4);
        var markers = Substitute.For<ISubscriptionUsageWarningMarkerRepository>();
        markers.ExistsAsync(
                subscription.Id,
                SubscriptionUsageResource.DRIVERS,
                subscription.Id.ToString("D"),
                Arg.Any<CancellationToken>())
            .Returns(false);
        SubscriptionUsageWarningMarker? marker = null;
        markers.AddAsync(
                Arg.Do<SubscriptionUsageWarningMarker>(value => marker = value),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<SubscriptionUsageWarningMarker>(0));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        Guid outboxEventId = Guid.Empty;
        string? payloadJson = null;
        outbox.EnqueueAsync(
                Arg.Do<Guid>(value => outboxEventId = value),
                "identity.subscription.usage_warning",
                Arg.Do<string>(value => payloadJson = value),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await CreatePublisher(markers, outbox).EnqueueIfThresholdCrossedAsync(
            subscription,
            SubscriptionPlan.CreateStarter(),
            SubscriptionUsageResource.DRIVERS,
            1,
            null,
            CancellationToken.None);

        marker.Should().NotBeNull();
        marker!.Id.Should().Be(outboxEventId);
        marker.SubscriptionId.Should().Be(subscription.Id);
        marker.Resource.Should().Be(SubscriptionUsageResource.DRIVERS);
        marker.PeriodKey.Should().Be(subscription.Id.ToString("D"));
        using var payload = JsonDocument.Parse(payloadJson!);
        payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(outboxEventId);
        payload.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(Now);
        payload.RootElement.GetProperty("subscriptionId").GetGuid().Should().Be(subscription.Id);
        payload.RootElement.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
        payload.RootElement.GetProperty("resource").GetString().Should().Be("DRIVERS");
        payload.RootElement.GetProperty("periodKey").GetString().Should().Be(subscription.Id.ToString("D"));
        payload.RootElement.GetProperty("used").GetInt32().Should().Be(4);
        payload.RootElement.GetProperty("limit").GetInt32().Should().Be(5);
        payload.RootElement.GetProperty("usagePercent").GetDecimal().Should().Be(80m);
    }

    [Fact]
    public async Task RecrossInSameResourceAndPeriod_WithDurableMarker_EmitsNothing()
    {
        var subscription = CreateSubscription(currentDrivers: 4);
        var markers = Substitute.For<ISubscriptionUsageWarningMarkerRepository>();
        markers.ExistsAsync(
                subscription.Id,
                SubscriptionUsageResource.DRIVERS,
                subscription.Id.ToString("D"),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var outbox = Substitute.For<IIntegrationEventOutbox>();

        await CreatePublisher(markers, outbox).EnqueueIfThresholdCrossedAsync(
            subscription,
            SubscriptionPlan.CreateStarter(),
            SubscriptionUsageResource.DRIVERS,
            1,
            null,
            CancellationToken.None);

        await markers.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await outbox.DidNotReceiveWithAnyArgs().EnqueueAsync(
            default,
            default!,
            default!,
            default);
    }

    [Fact]
    public async Task MonthlyTripCrossing_UsesCallerPeriodKeyForMonthBoundary()
    {
        var subscription = CreateSubscription(currentDrivers: 0);
        SetUsage(subscription, nameof(OperatorSubscription.CurrentTripsThisMonth), 80);
        var markers = Substitute.For<ISubscriptionUsageWarningMarkerRepository>();
        markers.ExistsAsync(
                subscription.Id,
                SubscriptionUsageResource.TRIPS_THIS_MONTH,
                "2026-08",
                Arg.Any<CancellationToken>())
            .Returns(false);
        SubscriptionUsageWarningMarker? marker = null;
        markers.AddAsync(
                Arg.Do<SubscriptionUsageWarningMarker>(value => marker = value),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<SubscriptionUsageWarningMarker>(0));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        outbox.EnqueueAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await CreatePublisher(markers, outbox).EnqueueIfThresholdCrossedAsync(
            subscription,
            SubscriptionPlan.CreateStarter(),
            SubscriptionUsageResource.TRIPS_THIS_MONTH,
            1,
            "2026-08",
            CancellationToken.None);

        marker.Should().NotBeNull();
        marker!.PeriodKey.Should().Be("2026-08");
    }

    private static SubscriptionUsageWarningPublisher CreatePublisher(
        ISubscriptionUsageWarningMarkerRepository markers,
        IIntegrationEventOutbox outbox)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new SubscriptionUsageWarningPublisher(markers, outbox, clock);
    }

    private static OperatorSubscription CreateSubscription(int currentDrivers)
    {
        var subscription = OperatorSubscription.CreateActiveTrial(
            OperatorId,
            SubscriptionPlan.StarterPlanId,
            Now.AddDays(-1),
            Now.AddDays(29));
        SetUsage(subscription, nameof(OperatorSubscription.CurrentDrivers), currentDrivers);
        return subscription;
    }

    private static void SetUsage(OperatorSubscription subscription, string propertyName, int value)
        => typeof(OperatorSubscription)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(subscription, value);
}
