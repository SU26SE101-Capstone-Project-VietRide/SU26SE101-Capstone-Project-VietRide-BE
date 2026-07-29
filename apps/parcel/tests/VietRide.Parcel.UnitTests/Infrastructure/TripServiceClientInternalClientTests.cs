using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure.Http;

namespace VietRide.Parcel.UnitTests.Infrastructure;

public class TripServiceClientInternalClientTests
{
    private static readonly Guid TripId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private FakeMessageHandler _handler = null!;

    [Fact]
    public async Task GetTripSummariesAsync_PostsDistinctIdsAndDeserializesRawArray()
    {
        var routeId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var departure = DateTimeOffset.UtcNow.AddHours(1);
        var arrival = departure.AddHours(8);
        var body = JsonSerializer.Serialize(new[]
        {
            new
            {
                tripId = TripId,
                status = "SCHEDULED",
                departureAt = departure,
                arrivalEstimate = arrival,
                route = new
                {
                    routeId,
                    name = "HCM - Da Lat",
                    originName = "Mien Dong",
                    destinationName = "Da Lat",
                },
                vehicle = new
                {
                    vehicleId,
                    licensePlate = "51B-12345",
                    status = "ACTIVE",
                },
                driverUserId = Guid.NewGuid(),
                assistantUserId = (Guid?)null,
            },
        }, JsonOptions);
        var client = BuildClient(HttpStatusCode.OK, body);

        var outcome = await client.GetTripSummariesAsync([TripId, TripId]);

        outcome.Kind.Should().Be(TripSummaryBatchOutcomeKind.Success);
        var summary = outcome.Summaries.Should().ContainSingle().Which;
        summary.Route.Should().Be(new TripRouteSummarySnapshot(
            routeId,
            "HCM - Da Lat",
            "Mien Dong",
            "Da Lat"));
        summary.Vehicle.Should().Be(new TripVehicleSummarySnapshot(
            vehicleId,
            "51B-12345",
            "ACTIVE"));
        _handler.LastRequest.Should().NotBeNull();
        _handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        _handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/internal/v1/trips/summaries/batch");
        var requestBody = await _handler.LastRequest.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(requestBody);
        json.RootElement.GetProperty("tripIds").GetArrayLength().Should().Be(1);
        json.RootElement.GetProperty("tripIds")[0].GetGuid().Should().Be(TripId);
    }

    [Fact]
    public async Task GetTripSummariesAsync_MapsNonSuccessToTransportFailure()
    {
        var client = BuildClient(HttpStatusCode.ServiceUnavailable, "{}");

        var outcome = await client.GetTripSummariesAsync([TripId]);

        outcome.Kind.Should().Be(TripSummaryBatchOutcomeKind.TransportError);
        outcome.Summaries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTripSummariesAsync_RejectsDuplicateResponseItemsAsTransportFailure()
    {
        var summary = new
        {
            tripId = TripId,
            status = "SCHEDULED",
            departureAt = DateTimeOffset.UtcNow,
            arrivalEstimate = DateTimeOffset.UtcNow.AddHours(1),
            route = new
            {
                routeId = Guid.NewGuid(),
                name = "Route",
                originName = "Origin",
                destinationName = "Destination",
            },
            vehicle = new
            {
                vehicleId = Guid.NewGuid(),
                licensePlate = "51B-12345",
                status = "ACTIVE",
            },
        };
        var client = BuildClient(HttpStatusCode.OK, JsonSerializer.Serialize(new[] { summary, summary }, JsonOptions));

        var outcome = await client.GetTripSummariesAsync([TripId]);

        outcome.Kind.Should().Be(TripSummaryBatchOutcomeKind.TransportError);
        outcome.Summaries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTripSummariesAsync_RejectsEmptyTripIdBeforeHttp()
    {
        var client = BuildClient(HttpStatusCode.OK, "[]");

        var action = () => client.GetTripSummariesAsync([Guid.Empty]);

        await action.Should().ThrowAsync<ArgumentException>();
        _handler.LastRequest.Should().BeNull();
    }

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
        var destinationArrivedAt = new DateTimeOffset(2026, 7, 15, 9, 30, 0, TimeSpan.Zero);
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
            destinationArrivedAt,
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.OK, snapshotJson);

        var result = await client.GetTripParcelSnapshotAsync(TripId);

        result.Kind.Should().Be(TripSnapshotOutcomeKind.Success);
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.TripId.Should().Be(TripId);
        result.Snapshot.Status.Should().Be("SCHEDULED");
        result.Snapshot.BaseFare.Should().Be(200_000);
        result.Snapshot.DestinationArrivedAt.Should().Be(destinationArrivedAt);
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

    [Fact]
    public async Task AuthorizeAssistantForTripAsync_UsesInternalAuthorizationEndpoint()
    {
        var userId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new
        {
            success = true,
            statusCode = 200,
            data = new { allowed = true, scope = "ASSISTANT", error = (string?)null },
        }, JsonOptions);
        var client = BuildClient(HttpStatusCode.OK, body);

        var result = await client.AuthorizeAssistantForTripAsync(TripId, userId, operatorId);

        result.Kind.Should().Be(TripCrewAuthorizationOutcomeKind.Authorized);
        _handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be($"/internal/v1/trips/{TripId:D}/tracking-authorization");
        _handler.LastRequest.RequestUri.Query.Should().Contain($"userId={userId:D}");
        _handler.LastRequest.RequestUri.Query.Should().Contain("role=ASSISTANT");
        _handler.LastRequest.RequestUri.Query.Should().Contain($"operatorId={operatorId:D}");
    }

    [Fact]
    public async Task ValidateRouteOwnershipAsync_UsesExpectedPathAndQuery_OnSuccess()
    {
        var routeId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var client = BuildClient(HttpStatusCode.OK, JsonSerializer.Serialize(new { routeId, operatorId }, JsonOptions));

        var result = await client.ValidateRouteOwnershipAsync(routeId, operatorId);

        result.Kind.Should().Be(RouteOwnershipOutcomeKind.Success);
        _handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        _handler.LastRequest.RequestUri!.AbsolutePath.Should().Be($"/internal/v1/routes/{routeId:D}/ownership");
        _handler.LastRequest.RequestUri.Query.Should().Be($"?operatorId={operatorId:D}");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, RouteOwnershipOutcomeKind.RouteNotFound)]
    [InlineData(HttpStatusCode.InternalServerError, RouteOwnershipOutcomeKind.TransportError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, RouteOwnershipOutcomeKind.TransportError)]
    public async Task ValidateRouteOwnershipAsync_MapsUpstreamStatus(
        HttpStatusCode status,
        RouteOwnershipOutcomeKind expected)
    {
        var client = BuildClient(status, "{}");

        var result = await client.ValidateRouteOwnershipAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Kind.Should().Be(expected);
    }

    [Fact]
    public async Task SearchAvailableParcelTripsAsync_Sends_Request_With_InvariantCulture()
    {
        var body = JsonSerializer.Serialize(new
        {
            items = Array.Empty<object>(),
            total = 0,
            page = 1,
            pageSize = 20,
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.OK, body);

        await client.SearchAvailableParcelTripsAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 7, 15),
            5.5m, ParcelSizeCategory.MEDIUM, 1, 20);

        _handler.LastRequest.Should().NotBeNull();
        var query = _handler.LastRequest!.RequestUri!.Query;
        query.Should().Contain("estimatedWeightKg=5.5");
        query.Should().NotContain("estimatedWeightKg=5,5");
    }

    [Fact]
    public async Task SearchAvailableParcelTripsAsync_DeserializesEnrichedProjection()
    {
        var routeId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var originId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var departure = new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.FromHours(7));
        var arrival = departure.AddHours(8);
        var body = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    tripId = TripId,
                    routeId,
                    operatorId,
                    operatorName = "VietRide Express",
                    status = "SCHEDULED",
                    originStation = new { id = originId, name = "Bến đi" },
                    destinationStation = new { id = destinationId, name = "Bến đến" },
                    departureDateTime = departure,
                    estimatedArrivalTime = arrival,
                    availableCargoWeightKg = 99m,
                    availableCargoVolumeM3 = 9.999m,
                },
            },
            page = 1,
            pageSize = 20,
            totalItems = 1,
        }, JsonOptions);
        var client = BuildClient(HttpStatusCode.OK, body);

        var result = await client.SearchAvailableParcelTripsAsync(
            originId,
            destinationId,
            new DateOnly(2026, 7, 27),
            1m,
            0.001m,
            ParcelSizeCategory.MEDIUM,
            1,
            20);

        var trip = result.Trips.Should().ContainSingle().Which;
        trip.Status.Should().Be("SCHEDULED");
        trip.OperatorId.Should().Be(operatorId);
        trip.OriginStation.Should().Be(new TripStationDto(originId, "Bến đi"));
        trip.DestinationStation.Should().Be(new TripStationDto(destinationId, "Bến đến"));
        trip.EstimatedArrivalTime.Should().Be(arrival);
        trip.AvailableCargoWeightKg.Should().Be(99m);
        trip.AvailableCargoVolumeM3.Should().Be(9.999m);
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
