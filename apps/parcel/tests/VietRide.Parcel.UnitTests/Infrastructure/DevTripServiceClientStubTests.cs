using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Infrastructure.Http;

namespace VietRide.Parcel.UnitTests.Infrastructure;

public class DevTripServiceClientStubTests
{
    private readonly DevTripServiceClient _sut = new(NullLogger<DevTripServiceClient>.Instance);

    [Fact]
    public async Task GetTripParcelSnapshotAsync_Returns_Success_With_Valid_Data()
    {
        var tripId = Guid.NewGuid();

        var result = await _sut.GetTripParcelSnapshotAsync(tripId);

        result.Kind.Should().Be(TripSnapshotOutcomeKind.Success);
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.TripId.Should().Be(tripId);
        result.Snapshot.Status.Should().Be("SCHEDULED");
        result.Snapshot.OriginStation.Name.Should().Be("Dev Origin");
        result.Snapshot.DestinationArrivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTripSummariesAsync_ReturnsOneCompleteSummaryPerDistinctTrip()
    {
        var tripId = Guid.NewGuid();

        var result = await _sut.GetTripSummariesAsync([tripId, tripId]);

        result.Kind.Should().Be(TripSummaryBatchOutcomeKind.Success);
        var summary = result.Summaries.Should().ContainSingle().Which;
        summary.TripId.Should().Be(tripId);
        summary.Route.Name.Should().Be("Dev Route");
        summary.Vehicle.LicensePlate.Should().Be("DEV-0001");
    }
}
