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
    public async Task Consumer_UsesCanonicalIdentityAndExactLegacyFallback_WhenDelivered()
    {
        var bookingId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var sender = new CapturingSender();
        var handler = new BookingCancelledIntegrationEventHandler(
            sender,
            NullLogger<BookingCancelledIntegrationEventHandler>.Instance);

        await handler.HandleAsync(Deserialize(CanonicalJson(eventId, bookingId)), CancellationToken.None);
        await handler.HandleAsync(Deserialize(LegacyJson(bookingId)), CancellationToken.None);

        var keys = sender.Requests.Cast<RefundToWalletCommand>().Select(command => command.IdempotencyKey);
        keys.Should().Equal(eventId.ToString("D"), bookingId.ToString("D"));
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

    private static BookingCancelledIntegrationEvent Deserialize(string json)
        => JsonSerializer.Deserialize<BookingCancelledIntegrationEvent>(json, JsonOptions)!;

    private static string CanonicalJson(Guid eventId, Guid bookingId) => $$"""
        {"eventId":"{{eventId}}","occurredAt":"2026-07-17T00:00:00Z","bookingId":"{{bookingId}}","userId":"33333333-3333-3333-3333-333333333333","refundAmount":1,"refundOverride":false,"cancellationReason":"USER","bookingCode":"VR1","ticketCodes":["T1"],"ticketCount":1}
        """;

    private static string LegacyJson(Guid bookingId) => $$"""
        {"bookingId":"{{bookingId}}","userId":"33333333-3333-3333-3333-333333333333","refundAmount":1,"refundOverride":false,"cancellationReason":"USER"}
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
