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
    public async Task Consumer_AcceptsLegacyCanonicalAndOperationalShapes_WhenDelivered()
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
        var operational = Deserialize(OperationalJson(eventId, bookingId));
        await handler.HandleAsync(operational, CancellationToken.None);

        sent.Select(command => (command.BookingId, command.DedupeId)).Should().Equal(
            (bookingId, eventId),
            (bookingId, bookingId),
            (bookingId, eventId));
        operational.TripId.Should().Be(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        operational.PreviousStatus.Should().Be("CONFIRMED");
        operational.SeatNumbers.Should().Equal("A01");
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

    [Theory]
    [InlineData("{\"eventId\":\"11111111-1111-1111-1111-111111111111\",\"occurredAt\":\"2026-07-17T00:00:00Z\",\"bookingId\":\"22222222-2222-2222-2222-222222222222\",\"userId\":\"33333333-3333-3333-3333-333333333333\",\"refundAmount\":1,\"refundOverride\":false,\"cancellationReason\":\"USER\",\"previousStatus\":\"CONFIRMED\",\"seatNumbers\":[\"A01\"]}")]
    [InlineData("{\"eventId\":\"11111111-1111-1111-1111-111111111111\",\"occurredAt\":\"2026-07-17T00:00:00Z\",\"bookingId\":\"22222222-2222-2222-2222-222222222222\",\"userId\":\"33333333-3333-3333-3333-333333333333\",\"refundAmount\":1,\"refundOverride\":false,\"cancellationReason\":\"USER\",\"tripId\":\"44444444-4444-4444-4444-444444444444\",\"seatNumbers\":[\"A01\"]}")]
    [InlineData("{\"eventId\":\"11111111-1111-1111-1111-111111111111\",\"occurredAt\":\"2026-07-17T00:00:00Z\",\"bookingId\":\"22222222-2222-2222-2222-222222222222\",\"userId\":\"33333333-3333-3333-3333-333333333333\",\"refundAmount\":1,\"refundOverride\":false,\"cancellationReason\":\"USER\",\"tripId\":\"44444444-4444-4444-4444-444444444444\",\"previousStatus\":\"CONFIRMED\"}")]
    public void Consumer_RejectsPartialOperationalShape(string json)
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

    private static string OperationalJson(Guid eventId, Guid bookingId) => $$"""
        {"eventId":"{{eventId}}","occurredAt":"2026-07-17T00:00:00Z","bookingId":"{{bookingId}}","userId":"33333333-3333-3333-3333-333333333333","refundAmount":0,"refundOverride":false,"cancellationReason":"USER","bookingCode":"VR1","ticketCodes":["T1"],"ticketCount":1,"tripId":"44444444-4444-4444-4444-444444444444","previousStatus":"CONFIRMED","seatNumbers":["A01"]}
        """;
}
