using System.Net;
using System.Text;
using FluentAssertions;
using VietRide.Trip.Infrastructure.ExternalClients;

namespace VietRide.Trip.UnitTests.ExternalClients;

public sealed class BookingImpactClientTests
{
    private static readonly Guid TripId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid OperatorId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    [Fact]
    public async Task GetTripEditImpactAsync_UsesExactPathAndOperatorQuery_AndReturnsMultipleImpacts()
    {
        var firstBookingId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var secondBookingId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        var handler = new CapturingJsonResponseHandler(HttpStatusCode.OK,
            $$"""
            {
              "tripId":"{{TripId:D}}",
              "activeBookingCount":2,
              "activeBookings":[
                {"bookingId":"{{firstBookingId:D}}","status":"PENDING_PAYMENT","seatNumbers":["A01","A02"]},
                {"bookingId":"{{secondBookingId:D}}","status":"CONFIRMED","seatNumbers":["B01"]}
              ]
            }
            """);
        using var httpClient = CreateHttpClient(handler);
        var client = new BookingImpactClient(httpClient);

        var result = await client.GetTripEditImpactAsync(TripId, OperatorId, CancellationToken.None);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.PathAndQuery.Should().Be(
            $"/internal/v1/bookings/trips/{TripId:D}/edit-impact?operatorId={OperatorId:D}");
        result.TripId.Should().Be(TripId);
        result.ActiveBookingCount.Should().Be(2);
        result.ActiveBookings.Should().BeEquivalentTo(
        [
            new
            {
                BookingId = firstBookingId,
                Status = "PENDING_PAYMENT",
                SeatNumbers = new[] { "A01", "A02" },
            },
            new
            {
                BookingId = secondBookingId,
                Status = "CONFIRMED",
                SeatNumbers = new[] { "B01" },
            },
        ], options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task GetTripEditImpactAsync_ReturnsEmptyProjection_On200Empty()
    {
        using var httpClient = CreateHttpClient(new JsonResponseHandler(HttpStatusCode.OK,
            $$"""
            {"tripId":"{{TripId:D}}","activeBookingCount":0,"activeBookings":[]}
            """));
        var client = new BookingImpactClient(httpClient);

        var result = await client.GetTripEditImpactAsync(TripId, OperatorId, CancellationToken.None);

        result.ActiveBookingCount.Should().Be(0);
        result.ActiveBookings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTripEditImpactAsync_ThrowsHttpRequestException_On404()
    {
        using var httpClient = CreateHttpClient(
            new JsonResponseHandler(HttpStatusCode.NotFound, "{}"));
        var client = new BookingImpactClient(httpClient);

        Func<Task> act = () => client.GetTripEditImpactAsync(
            TripId,
            OperatorId,
            CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetTripEditImpactAsync_ThrowsHttpRequestException_OnInvalidProjection()
    {
        using var httpClient = CreateHttpClient(new JsonResponseHandler(HttpStatusCode.OK,
            $$"""
            {"tripId":"{{TripId:D}}","activeBookingCount":1,"activeBookings":[]}
            """));
        var client = new BookingImpactClient(httpClient);

        Func<Task> act = () => client.GetTripEditImpactAsync(
            TripId,
            OperatorId,
            CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*invalid data*");
    }

    [Fact]
    public async Task GetTripEditImpactAsync_PropagatesCancellation()
    {
        var handler = new CancellationHandler();
        using var httpClient = CreateHttpClient(handler);
        var client = new BookingImpactClient(httpClient);
        using var cancellation = new CancellationTokenSource();

        var request = client.GetTripEditImpactAsync(TripId, OperatorId, cancellation.Token);
        await handler.Started;
        cancellation.Cancel();

        Func<Task> act = () => request;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetActiveBookingCountByStopAsync_PreservesExistingSeam()
    {
        var stopId = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
        var handler = new CapturingJsonResponseHandler(
            HttpStatusCode.OK,
            "{\"activeBookingCount\":3}");
        using var httpClient = CreateHttpClient(handler);
        var client = new BookingImpactClient(httpClient);

        var result = await client.GetActiveBookingCountByStopAsync(
            stopId,
            OperatorId,
            CancellationToken.None);

        result.Should().Be(3);
        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be(
            $"/internal/v1/bookings/active-by-stop/{stopId:D}/count?operatorId={OperatorId:D}");
    }

    [Fact]
    public async Task GetTripEditImpactAsync_RejectsEmptyOperatorId_BeforeSendingRequest()
    {
        var handler = new CapturingJsonResponseHandler(HttpStatusCode.OK, "{}");
        using var httpClient = CreateHttpClient(handler);
        var client = new BookingImpactClient(httpClient);

        Func<Task> act = () => client.GetTripEditImpactAsync(
            TripId,
            Guid.Empty,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("operatorId");
        handler.LastRequest.Should().BeNull();
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler)
        => new(handler)
        {
            BaseAddress = new Uri("http://booking.local"),
        };

    private class JsonResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode statusCode;
        private readonly string json;

        public JsonResponseHandler(HttpStatusCode statusCode, string json)
        {
            this.statusCode = statusCode;
            this.json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class CapturingJsonResponseHandler : JsonResponseHandler
    {
        public CapturingJsonResponseHandler(HttpStatusCode statusCode, string json)
            : base(statusCode, json)
        {
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation token should stop the request.");
        }
    }
}
