using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;
using VietRide.Payment.Infrastructure.Messaging;

namespace VietRide.Payment.UnitTests.Infrastructure.Messaging;

public sealed class Day23BookingCancelledCompatibilityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Consumer_AcceptsLegacyCanonicalAndOperationalShapes_WhenDelivered()
    {
        var bookingId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var sender = new CapturingSender();
        var handler = new BookingCancelledIntegrationEventHandler(
            sender,
            NullLogger<BookingCancelledIntegrationEventHandler>.Instance);

        await handler.HandleAsync(Deserialize(CanonicalJson(eventId, bookingId)), CancellationToken.None);
        await handler.HandleAsync(Deserialize(LegacyJson(bookingId)), CancellationToken.None);
        var operational = Deserialize(OperationalJson(eventId, bookingId));
        await handler.HandleAsync(operational, CancellationToken.None);

        var keys = sender.Requests.Cast<RefundToWalletCommand>().Select(command => command.IdempotencyKey);
        keys.Should().Equal(eventId.ToString("D"), bookingId.ToString("D"), eventId.ToString("D"));
        operational.TripId.Should().Be(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        operational.PreviousStatus.Should().Be("CONFIRMED");
        operational.SeatNumbers.Should().Equal("A01");
    }

    [Theory]
    [InlineData("{\"eventId\":\"11111111-1111-1111-1111-111111111111\"}")]
    [InlineData("{\"bookingId\":\"11111111-1111-1111-1111-111111111111\",\"userId\":\"22222222-2222-2222-2222-222222222222\",\"refundAmount\":1,\"refundOverride\":false,\"cancellationReason\":\"USER\",\"unexpected\":true}")]
    public void Consumer_RejectsPartialOrExtraPayloadBeforeDelivery(string json)
    {
        var act = () => Deserialize(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public async Task Consumer_RejectsEmptyIdentityAndRedeliveryUsesOneDedupeKey()
    {
        var bookingId = Guid.NewGuid();
        var sender = new CapturingSender();
        var handler = new BookingCancelledIntegrationEventHandler(
            sender,
            NullLogger<BookingCancelledIntegrationEventHandler>.Instance);
        var malformed = new BookingCancelledIntegrationEvent
        {
            EventId = Guid.Empty,
            OccurredAtOffset = DateTimeOffset.UtcNow,
            BookingId = bookingId,
            UserId = Guid.NewGuid(),
            RefundAmount = 1,
            RefundOverride = false,
            CancellationReason = "USER",
        };

        await FluentActions.Awaiting(() => handler.HandleAsync(malformed, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();

        var canonical = Deserialize(CanonicalJson(Guid.NewGuid(), bookingId));
        await handler.HandleAsync(canonical, CancellationToken.None);
        await handler.HandleAsync(canonical, CancellationToken.None);
        sender.Requests.Cast<RefundToWalletCommand>()
            .Select(command => command.IdempotencyKey)
            .Distinct()
            .Should().ContainSingle();
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
    [InlineData("{\"eventId\":\"11111111-1111-1111-1111-111111111111\",\"occurredAt\":\"2026-07-17T00:00:00Z\",\"bookingId\":\"22222222-2222-2222-2222-222222222222\",\"userId\":\"33333333-3333-3333-3333-333333333333\",\"refundAmount\":1,\"refundOverride\":false,\"cancellationReason\":\"USER\",\"tripId\":\"00000000-0000-0000-0000-000000000000\",\"previousStatus\":\"CONFIRMED\",\"seatNumbers\":[\"A01\"]}")]
    [InlineData("{\"eventId\":\"11111111-1111-1111-1111-111111111111\",\"occurredAt\":\"2026-07-17T00:00:00Z\",\"bookingId\":\"22222222-2222-2222-2222-222222222222\",\"userId\":\"33333333-3333-3333-3333-333333333333\",\"refundAmount\":1,\"refundOverride\":false,\"cancellationReason\":\"USER\",\"tripId\":\"44444444-4444-4444-4444-444444444444\",\"previousStatus\":\"CANCELLED\",\"seatNumbers\":[\"A01\"]}")]
    [InlineData("{\"eventId\":\"11111111-1111-1111-1111-111111111111\",\"occurredAt\":\"2026-07-17T00:00:00Z\",\"bookingId\":\"22222222-2222-2222-2222-222222222222\",\"userId\":\"33333333-3333-3333-3333-333333333333\",\"refundAmount\":1,\"refundOverride\":false,\"cancellationReason\":\"USER\",\"tripId\":\"44444444-4444-4444-4444-444444444444\",\"previousStatus\":\"CONFIRMED\",\"seatNumbers\":[\" \" ]}")]
    [InlineData("{\"eventId\":\"11111111-1111-1111-1111-111111111111\",\"occurredAt\":\"2026-07-17T00:00:00Z\",\"bookingId\":\"22222222-2222-2222-2222-222222222222\",\"userId\":\"33333333-3333-3333-3333-333333333333\",\"refundAmount\":1,\"refundOverride\":false,\"cancellationReason\":\"USER\",\"tripId\":\"44444444-4444-4444-4444-444444444444\",\"previousStatus\":\"CONFIRMED\",\"seatNumbers\":null}")]
    public void Consumer_RejectsMalformedOperationalShape(string json)
    {
        var act = () => Deserialize(json).Validate();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Consumer_RejectsLegacyIdentityWithOperationalFields()
    {
        var bookingId = Guid.NewGuid();
        var json = $$"""
            {"bookingId":"{{bookingId}}","userId":"33333333-3333-3333-3333-333333333333","refundAmount":1,"refundOverride":false,"cancellationReason":"USER","tripId":"44444444-4444-4444-4444-444444444444","previousStatus":"CONFIRMED","seatNumbers":["A01"]}
            """;

        var act = () => Deserialize(json).Validate();

        act.Should().Throw<ArgumentException>();
    }

    private static BookingCancelledIntegrationEvent Deserialize(string json)
        => JsonSerializer.Deserialize<BookingCancelledIntegrationEvent>(json, JsonOptions)!;

    private static string CanonicalJson(Guid eventId, Guid bookingId) => $$"""
        {"eventId":"{{eventId}}","occurredAt":"2026-07-17T00:00:00Z","bookingId":"{{bookingId}}","userId":"33333333-3333-3333-3333-333333333333","refundAmount":1,"refundOverride":false,"cancellationReason":"USER","bookingCode":"VR1","ticketCodes":["T1"],"ticketCount":1}
        """;

    private static string LegacyJson(Guid bookingId) => $$"""
        {"bookingId":"{{bookingId}}","userId":"33333333-3333-3333-3333-333333333333","refundAmount":1,"refundOverride":false,"cancellationReason":"USER"}
        """;

    private static string OperationalJson(Guid eventId, Guid bookingId) => $$"""
        {"eventId":"{{eventId}}","occurredAt":"2026-07-17T00:00:00Z","bookingId":"{{bookingId}}","userId":"33333333-3333-3333-3333-333333333333","refundAmount":1,"refundOverride":false,"cancellationReason":"USER","bookingCode":"VR1","ticketCodes":["T1"],"ticketCount":1,"tripId":"44444444-4444-4444-4444-444444444444","previousStatus":"CONFIRMED","seatNumbers":["A01"]}
        """;

    private sealed class CapturingSender : ISender
    {
        public List<object> Requests { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult((TResponse)(object)new RefundToWalletResult(Guid.NewGuid(), 1));
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => Empty<object?>();

        private static async IAsyncEnumerable<T> Empty<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
