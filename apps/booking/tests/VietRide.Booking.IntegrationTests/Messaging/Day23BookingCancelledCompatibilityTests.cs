using System.Text.Json;
using FluentAssertions;
using MediatR;
using NSubstitute;
using VietRide.Booking.Application.Features.BookingStats.UpdateBookingStats;
using VietRide.Booking.Infrastructure.Messaging;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.IntegrationTests.Messaging;

public sealed class Day23BookingCancelledCompatibilityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Consumer_UsesCanonicalIdentityAndExactLegacyFallback_WhenDelivered()
    {
        var bookingId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        var sent = new List<UpdateBookingStatsCommand>();
        mediator.Send(Arg.Do<UpdateBookingStatsCommand>(command => sent.Add(command)), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        var handler = CreateHandler(mediator);

        await handler.HandleAsync(Deserialize(CanonicalJson(eventId, bookingId)), CancellationToken.None);
        await handler.HandleAsync(Deserialize(LegacyJson(bookingId)), CancellationToken.None);

        sent.Select(command => (command.BookingId, command.DedupeId)).Should().Equal(
            (bookingId, eventId),
            (bookingId, bookingId));
    }

    [Theory]
    [InlineData("{\"eventId\":\"11111111-1111-1111-1111-111111111111\"}")]
    [InlineData("{\"bookingId\":\"11111111-1111-1111-1111-111111111111\",\"userId\":\"22222222-2222-2222-2222-222222222222\",\"refundAmount\":0,\"refundOverride\":false,\"cancellationReason\":\"USER\",\"unexpected\":true}")]
    public void Consumer_RejectsPartialOrExtraPayloadBeforeDelivery(string json)
    {
        var act = () => Deserialize(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public async Task Consumer_RejectsEmptyIdentityAndMalformedRequiredValues_WhenDelivered()
    {
        var handler = CreateHandler(Substitute.For<IMediator>());
        var malformed = new BookingCancelledIntegrationEvent
        {
            EventId = Guid.Empty,
            OccurredAtOffset = DateTimeOffset.UtcNow,
            BookingId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            RefundAmount = 1,
            RefundOverride = false,
            CancellationReason = "USER",
        };

        var act = () => handler.HandleAsync(malformed, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("{\"eventId\":null,\"bookingId\":\"11111111-1111-1111-1111-111111111111\",\"userId\":\"22222222-2222-2222-2222-222222222222\",\"refundAmount\":1,\"refundOverride\":false,\"cancellationReason\":\"USER\"}")]
    [InlineData("{\"occurredAt\":null,\"bookingId\":\"11111111-1111-1111-1111-111111111111\",\"userId\":\"22222222-2222-2222-2222-222222222222\",\"refundAmount\":1,\"refundOverride\":false,\"cancellationReason\":\"USER\"}")]
    [InlineData("{\"eventId\":null,\"occurredAt\":null,\"bookingId\":\"11111111-1111-1111-1111-111111111111\",\"userId\":\"22222222-2222-2222-2222-222222222222\",\"refundAmount\":1,\"refundOverride\":false,\"cancellationReason\":\"USER\"}")]
    public void Consumer_RejectsExplicitNullIdentityProperties(string json)
    {
        var act = () => Deserialize(json).Validate();

        act.Should().Throw<ArgumentException>();
    }

    private static BookingCancelledIntegrationEvent Deserialize(string json)
        => JsonSerializer.Deserialize<BookingCancelledIntegrationEvent>(json, JsonOptions)!;

    private static IIntegrationEventHandler<BookingCancelledIntegrationEvent> CreateHandler(IMediator mediator)
    {
        var type = typeof(BookingCancelledIntegrationEvent).Assembly.GetType(
            "VietRide.Booking.Infrastructure.Messaging.BookingCancelledIntegrationEventHandler",
            throwOnError: true)!;
        return (IIntegrationEventHandler<BookingCancelledIntegrationEvent>)Activator.CreateInstance(type, mediator)!;
    }

    private static string CanonicalJson(Guid eventId, Guid bookingId) => $$"""
        {"eventId":"{{eventId}}","occurredAt":"2026-07-17T00:00:00Z","bookingId":"{{bookingId}}","userId":"33333333-3333-3333-3333-333333333333","refundAmount":0,"refundOverride":false,"cancellationReason":"USER"}
        """;

    private static string LegacyJson(Guid bookingId) => $$"""
        {"bookingId":"{{bookingId}}","userId":"33333333-3333-3333-3333-333333333333","refundAmount":0,"refundOverride":false,"cancellationReason":"USER"}
        """;
}
