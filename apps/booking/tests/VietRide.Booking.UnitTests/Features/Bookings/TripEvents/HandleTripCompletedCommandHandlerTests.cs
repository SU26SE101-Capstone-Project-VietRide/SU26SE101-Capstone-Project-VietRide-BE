using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Bookings.TripEvents;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure.DependencyInjection;
using VietRide.Booking.Infrastructure.Messaging;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Messaging.RabbitMq;

namespace VietRide.Booking.UnitTests.Features.Bookings.TripEvents;

public sealed class HandleTripCompletedCommandHandlerTests
{
    private static readonly Guid TripId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset CompletedAt =
        new(2026, 7, 14, 18, 30, 45, TimeSpan.FromHours(7));

    [Fact]
    public void Contract_DeserializesFrozenPayload()
    {
        var integrationEvent = JsonSerializer.Deserialize<TripCompletedIntegrationEvent>(
            """
            {
              "tripId": "11111111-1111-1111-1111-111111111111",
              "completedAt": "2026-07-14T18:30:45+07:00",
              "hasSubstitution": true
            }
            """,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        integrationEvent.Should().NotBeNull();
        integrationEvent!.TripId.Should().Be(TripId);
        integrationEvent.CompletedAt.Should().Be(CompletedAt);
        integrationEvent.HasSubstitution.Should().BeTrue();
        integrationEvent.OccurredAt.Should().Be(CompletedAt.UtcDateTime);
        TripCompletedIntegrationEvent.EventType.Should().Be("trip.trip.completed");
    }

    [Fact]
    public async Task FirstDelivery_AppendsOneApprovedHistoryPerTransitionedBooking()
    {
        var firstBookingId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var secondBookingId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var bookings = Substitute.For<IBookingRepository>();
        var history = Substitute.For<IBookingStatusHistoryRepository>();
        bookings.TryCompleteEligibleByTripIdAsync(TripId, CompletedAt, Arg.Any<CancellationToken>())
            .Returns([secondBookingId, firstBookingId]);
        var handler = new HandleTripCompletedCommandHandler(bookings, history);

        var changed = await handler.Handle(
            new HandleTripCompletedCommand(TripId, CompletedAt, HasSubstitution: true),
            CancellationToken.None);

        changed.Should().Be(2);
        await history.Received(1).AddAsync(
            Arg.Is<BookingStatusHistory>(row =>
                row.BookingId == firstBookingId
                && row.Status == BookingStatus.COMPLETED
                && row.OccurredAt == CompletedAt
                && row.Source == BookingStatusHistorySource.CompleteOnTripCompleted
                && row.ActorUserId == null
                && row.ReasonCode == null),
            Arg.Any<CancellationToken>());
        await history.Received(1).AddAsync(
            Arg.Is<BookingStatusHistory>(row =>
                row.BookingId == secondBookingId
                && row.Status == BookingStatus.COMPLETED
                && row.OccurredAt == CompletedAt
                && row.Source == BookingStatusHistorySource.CompleteOnTripCompleted
                && row.ActorUserId == null
                && row.ReasonCode == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateDelivery_AddsNoHistory()
    {
        var bookings = Substitute.For<IBookingRepository>();
        var history = Substitute.For<IBookingStatusHistoryRepository>();
        bookings.TryCompleteEligibleByTripIdAsync(TripId, CompletedAt, Arg.Any<CancellationToken>())
            .Returns([]);
        var handler = new HandleTripCompletedCommandHandler(bookings, history);

        var changed = await handler.Handle(
            new HandleTripCompletedCommand(TripId, CompletedAt, HasSubstitution: false),
            CancellationToken.None);

        changed.Should().Be(0);
        await history.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public void Consumer_RegistersOneStableQueueAndBinding()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trip:UseDevStub"] = "true",
                ["Identity:UseDevStub"] = "true",
                ["Payment:UseDevStub"] = "true",
                ["REDIS_URL"] = "localhost:6379",
                ["RabbitMq:ExchangeName"] = "vietride.events",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVietRideMessaging(configuration);
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        services.Count(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(RabbitMqConsumerBackgroundService<TripCompletedIntegrationEvent>))
            .Should().Be(1);
        var options = provider
            .GetRequiredService<IOptions<RabbitMqConsumerOptions<TripCompletedIntegrationEvent>>>()
            .Value.Value;
        options.QueueName.Should().Be("booking.trip-completed");
        options.BindingKeys.Should().Equal(TripCompletedIntegrationEvent.EventType);
    }
}
