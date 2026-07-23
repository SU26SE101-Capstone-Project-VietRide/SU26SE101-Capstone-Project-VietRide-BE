using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Parcel.Infrastructure.Http;

namespace VietRide.Parcel.UnitTests.Infrastructure;

public sealed class Day29TripServiceClientCargoIdempotencyTests
{
    [Fact]
    public async Task RepeatedLoadUsesStableActionParcelIdempotencyIdentity()
    {
        var tripId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        var handler = new RecordingHandler();
        var client = new TripServiceClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://trip-service") },
            NullLogger<TripServiceClient>.Instance);

        await client.LoadCargoAsync(tripId, parcelId, 12.5m, 0.25m);
        await client.LoadCargoAsync(tripId, parcelId, 12.5m, 0.25m);

        handler.Requests.Should().HaveCount(2);
        var expectedIdentity = parcelId.ToString("D");
        handler.Requests.Should().OnlyContain(request =>
            request.IdempotencyKey == expectedIdentity
            && request.BodyIdempotencyKey == expectedIdentity
            && request.Path == $"/internal/v1/trips/{tripId:D}/cargo/load");
    }

    [Fact]
    public async Task ExplicitOperationKeyIsForwardedToHeaderAndBody()
    {
        var tripId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var handler = new RecordingHandler();
        var client = new TripServiceClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://trip-service") },
            NullLogger<TripServiceClient>.Instance);

        await client.RemeasureCargoAsync(
            tripId,
            parcelId,
            12.5m,
            0.25m,
            allowCapacityOverflow: false,
            operationId);

        handler.Requests.Should().ContainSingle(request =>
            request.IdempotencyKey == operationId.ToString("D")
            && request.BodyIdempotencyKey == operationId.ToString("D")
            && request.Path == $"/internal/v1/trips/{tripId:D}/cargo/remeasure");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            Requests.Add(new RecordedRequest(
                request.RequestUri!.AbsolutePath,
                request.Headers.GetValues("Idempotency-Key").Single(),
                json.RootElement.GetProperty("idempotencyKey").GetString()));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            };
        }
    }

    private sealed record RecordedRequest(
        string Path,
        string IdempotencyKey,
        string? BodyIdempotencyKey);
}
