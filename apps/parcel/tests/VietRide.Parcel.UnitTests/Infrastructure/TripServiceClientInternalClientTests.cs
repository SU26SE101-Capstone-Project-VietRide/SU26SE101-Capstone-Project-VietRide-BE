using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Infrastructure.Http;

namespace VietRide.Parcel.UnitTests.Infrastructure;

public class TripServiceClientInternalClientTests
{
    private static readonly Guid TripId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private FakeMessageHandler _handler = null!;

    [Fact]
    public async Task GetTripParcelSnapshotAsync_Sends_Request_To_Correct_Path()
    {
        var snapshotJson = JsonSerializer.Serialize(new
        {
            tripId = TripId,
            operatorId = Guid.NewGuid(),
            routeId = Guid.NewGuid(),
            vehicleId = Guid.NewGuid(),
            status = "SCHEDULED",
            departureDateTime = DateTimeOffset.UtcNow.AddDays(1),
            estimatedArrivalTime = DateTimeOffset.UtcNow.AddDays(1).AddHours(8),
            baseFare = 200_000L,
            originStation = new { id = Guid.NewGuid(), name = "Origin" },
            destinationStation = new { id = Guid.NewGuid(), name = "Destination" },
            stops = Array.Empty<object>(),
            seatSummary = new { totalSeats = 40, availableSeats = 20 },
            returnRouteId = (Guid?)null,
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.OK, snapshotJson);

        await client.GetTripParcelSnapshotAsync(TripId);

        _handler.LastRequest.Should().NotBeNull();
        _handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be($"/internal/v1/trips/{TripId:D}");
        _handler.LastRequest.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task GetTripParcelSnapshotAsync_Returns_Success_On_200()
    {
        var snapshotJson = JsonSerializer.Serialize(new
        {
            tripId = TripId,
            operatorId = Guid.NewGuid(),
            routeId = Guid.NewGuid(),
            vehicleId = Guid.NewGuid(),
            status = "SCHEDULED",
            departureDateTime = DateTimeOffset.UtcNow.AddDays(1),
            estimatedArrivalTime = DateTimeOffset.UtcNow.AddDays(1).AddHours(8),
            baseFare = 200_000L,
            originStation = new { id = Guid.NewGuid(), name = "Origin" },
            destinationStation = new { id = Guid.NewGuid(), name = "Destination" },
            stops = Array.Empty<object>(),
            seatSummary = new { totalSeats = 40, availableSeats = 20 },
            returnRouteId = (Guid?)null,
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.OK, snapshotJson);

        var result = await client.GetTripParcelSnapshotAsync(TripId);

        result.Kind.Should().Be(TripSnapshotOutcomeKind.Success);
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.TripId.Should().Be(TripId);
        result.Snapshot.Status.Should().Be("SCHEDULED");
        result.Snapshot.BaseFare.Should().Be(200_000);
    }

    [Fact]
    public async Task GetTripParcelSnapshotAsync_Returns_TripNotFound_On_404()
    {
        var client = BuildClient(HttpStatusCode.NotFound, "{}");

        var result = await client.GetTripParcelSnapshotAsync(TripId);

        result.Kind.Should().Be(TripSnapshotOutcomeKind.TripNotFound);
        result.Snapshot.Should().BeNull();
    }

    [Fact]
    public async Task GetTripParcelSnapshotAsync_Returns_TransportError_On_5xx()
    {
        var client = BuildClient(HttpStatusCode.InternalServerError, "{}");

        var result = await client.GetTripParcelSnapshotAsync(TripId);

        result.Kind.Should().Be(TripSnapshotOutcomeKind.TransportError);
    }

    private TripServiceClient BuildClient(HttpStatusCode status, string body)
    {
        _handler = new FakeMessageHandler(status, body);
        var httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("http://trip-service"),
        };
        return new TripServiceClient(httpClient, NullLogger<TripServiceClient>.Instance);
    }
}
