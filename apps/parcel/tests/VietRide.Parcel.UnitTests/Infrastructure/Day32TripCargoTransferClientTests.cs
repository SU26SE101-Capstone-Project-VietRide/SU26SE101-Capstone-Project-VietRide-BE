using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Infrastructure.Http;

namespace VietRide.Parcel.UnitTests.Infrastructure;

public sealed class Day32TripCargoTransferClientTests
{
    [Fact]
    public async Task TransferCargo_SendsExactContractAndUuidV4IdempotencyKey()
    {
        var sourceTripId = Guid.NewGuid();
        var targetTripId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json($$"""
                {
                  "success": true,
                  "statusCode": 200,
                  "data": {
                    "parcelId": "{{parcelId:D}}",
                    "sourceTripId": "{{sourceTripId:D}}",
                    "targetTripId": "{{targetTripId:D}}",
                    "targetState": "RESERVED",
                    "weightKg": 12.5,
                    "volumeM3": 0.08
                  },
                  "meta": { "traceId": "test", "timestamp": "2026-07-30T00:00:00Z" }
                }
                """),
        });
        var client = new TripServiceClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://trip") },
            NullLogger<TripServiceClient>.Instance);

        var outcome = await client.TransferCargoAsync(
            sourceTripId,
            parcelId,
            targetTripId,
            "RESERVED",
            allowCapacityOverflow: false,
            idempotencyKey,
            CancellationToken.None);

        outcome.Kind.Should().Be(TripCargoTransferOutcomeKind.Success);
        handler.Path.Should().Be(
            $"/internal/v1/trips/{sourceTripId:D}/cargo/transfer");
        handler.IdempotencyKey.Should().Be(idempotencyKey.ToString("D"));
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
                "parcelId",
                "targetTripId",
                "targetState",
                "allowCapacityOverflow");
        body.RootElement.GetProperty("targetState").GetString()
            .Should().Be("RESERVED");
        body.RootElement.GetProperty("allowCapacityOverflow").GetBoolean()
            .Should().BeFalse();
    }

    [Fact]
    public async Task TransferCargo_MapsCapacityRejectionWithoutTreatingItAsTransportSuccess()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = Json("""
                    {
                      "success": false,
                      "statusCode": 422,
                      "error": {
                        "code": "TRIP_CARGO_CAPACITY_EXCEEDED",
                        "message": "full"
                      },
                      "meta": { "traceId": "test", "timestamp": "2026-07-30T00:00:00Z" }
                    }
                    """),
            });
        var client = new TripServiceClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://trip") },
            NullLogger<TripServiceClient>.Instance);

        var outcome = await client.TransferCargoAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "RESERVED",
            false,
            Guid.NewGuid(),
            CancellationToken.None);

        outcome.Kind.Should().Be(TripCargoTransferOutcomeKind.CapacityExceeded);
    }

    [Fact]
    public async Task TransferCargo_IdempotencyPendingConflict_RemainsUnknownForClaimRecovery()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(
            HttpStatusCode.Conflict)
        {
            Content = Json("""
                {
                  "success": false,
                  "statusCode": 409,
                  "error": {
                    "code": "IDEMPOTENCY_REQUEST_PENDING",
                    "message": "still processing"
                  },
                  "meta": { "traceId": "trace" }
                }
                """),
        });
        var client = new TripServiceClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://trip") },
            NullLogger<TripServiceClient>.Instance);

        var outcome = await client.TransferCargoAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "LOADED",
            allowCapacityOverflow: true,
            Guid.NewGuid(),
            CancellationToken.None);

        outcome.Kind.Should().Be(TripCargoTransferOutcomeKind.TransportError);
    }

    private static StringContent Json(string value)
        => new(value, Encoding.UTF8, "application/json");

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        public string? Path { get; private set; }

        public string? IdempotencyKey { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            IdempotencyKey = request.Headers.TryGetValues(
                "Idempotency-Key",
                out var values)
                ? values.Single()
                : null;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response(request);
        }
    }
}
