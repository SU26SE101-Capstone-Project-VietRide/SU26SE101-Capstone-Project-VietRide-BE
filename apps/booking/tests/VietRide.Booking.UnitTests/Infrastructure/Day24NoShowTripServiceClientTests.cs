using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using VietRide.Booking.Application.Exceptions;
using VietRide.Booking.Infrastructure.Http;

namespace VietRide.Booking.UnitTests.Infrastructure;

public sealed class Day24NoShowTripServiceClientTests
{
    public static TheoryData<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> Failures => new()
    {
        (_, _) => throw new HttpRequestException("transport"),
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{malformed", Encoding.UTF8, "application/json"),
        }),
        (_, _) => throw new TaskCanceledException("timeout"),
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task OperationalSnapshot_NormalizesFailureToRegisteredUpstreamError(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
    {
        var client = new TripServiceClient(
            new HttpClient(new StubHandler(response)) { BaseAddress = new Uri("http://trip") },
            Substitute.For<ILogger<TripServiceClient>>());

        var act = () => client.GetOperationalTripSnapshotAsync(Guid.NewGuid(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<BookingUpstreamUnavailableException>();
        exception.Which.StatusCode.Should().Be(502);
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => response(request, cancellationToken);
    }
}
