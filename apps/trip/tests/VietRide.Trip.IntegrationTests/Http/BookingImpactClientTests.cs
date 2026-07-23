using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using VietRide.Trip.Infrastructure.Http;
using Xunit;

namespace VietRide.Trip.IntegrationTests.Http;

public sealed class BookingImpactClientTests
{
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();

    [Fact]
    public async Task SuccessReturnsAffectedBookings()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { tripId = TripId, activeBookingCount = 1, activeBookings = new[] { new { bookingId = Guid.NewGuid(), status = "CONFIRMED", seatNumbers = new[] { "A1" } } } })
        });
        var client = new BookingImpactClient(new HttpClient(handler) { BaseAddress = new Uri("http://booking") }, Options.Create(new BookingImpactClientOptions()));
        var result = await client.GetTripEditImpactAsync(TripId, OperatorId, default);
        result.ActiveBookingCount.Should().Be(1);
        handler.Request!.RequestUri!.PathAndQuery.Should().Be(
            $"/internal/v1/bookings/trips/{TripId:D}/edit-impact?operatorId={OperatorId:D}");

        var malformedHandler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json", System.Text.Encoding.UTF8, "application/json"),
        });
        var malformedClient = new BookingImpactClient(
            new HttpClient(malformedHandler) { BaseAddress = new Uri("http://booking") },
            Options.Create(new BookingImpactClientOptions()));

        var malformed = () => malformedClient.GetTripEditImpactAsync(TripId, OperatorId, default);
        await malformed.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Booking Trip-edit impact returned malformed JSON.");

        var duplicateBookingId = Guid.NewGuid();
        await AssertInvalidResponseAsync(new
        {
            tripId = TripId,
            activeBookingCount = 2,
            activeBookings = new[]
            {
                new { bookingId = duplicateBookingId, status = "CONFIRMED", seatNumbers = new[] { "A1" } },
                new { bookingId = duplicateBookingId, status = "PENDING_PAYMENT", seatNumbers = new[] { "A2" } },
            },
        });
        await AssertInvalidResponseAsync(new
        {
            tripId = TripId,
            activeBookingCount = 1,
            activeBookings = new[]
            {
                new { bookingId = Guid.NewGuid(), status = "CONFIRMED", seatNumbers = new[] { "A1", "a1" } },
            },
        });
        await AssertInvalidResponseAsync(new
        {
            tripId = Guid.NewGuid(),
            activeBookingCount = 0,
            activeBookings = Array.Empty<object>(),
        });
        await AssertInvalidResponseAsync(new
        {
            tripId = TripId,
            activeBookingCount = 1,
            activeBookings = new[]
            {
                new { bookingId = Guid.NewGuid(), status = "CANCELLED", seatNumbers = new[] { "A1" } },
            },
        });
    }

    [Fact]
    public async Task RejectsMissingInternalJwt()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = new BookingImpactClient(new HttpClient(handler) { BaseAddress = new Uri("http://booking") }, Options.Create(new BookingImpactClientOptions()));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetTripEditImpactAsync(TripId, OperatorId, default));
    }

    [Fact]
    public async Task RejectsForeignTenant()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = new BookingImpactClient(new HttpClient(handler) { BaseAddress = new Uri("http://booking") }, Options.Create(new BookingImpactClientOptions()));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetTripEditImpactAsync(TripId, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task TimeoutFailsClosed()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timeout"));
        var options = Options.Create(new BookingImpactClientOptions { Timeout = TimeSpan.FromMilliseconds(1) });
        var client = new BookingImpactClient(new HttpClient(handler) { BaseAddress = new Uri("http://booking") }, options);
        await Assert.ThrowsAnyAsync<Exception>(() => client.GetTripEditImpactAsync(TripId, OperatorId, default));
    }

    private static async Task AssertInvalidResponseAsync<TResponse>(TResponse payload)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload),
        });
        var client = new BookingImpactClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://booking") },
            Options.Create(new BookingImpactClientOptions()));

        var action = () => client.GetTripEditImpactAsync(TripId, OperatorId, default);
        await action.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Booking Trip-edit impact returned invalid data.");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> sync = response;
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(sync(request));
        }
    }
}
