using FluentAssertions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.Trips.BatchTripSummaries;
using VietRide.Trip.Application.Features.Internal.Trips.ForwardingOptions;
using VietRide.Trip.UnitTests.Features.DriverSchedules;

namespace VietRide.Trip.UnitTests.Features.Internal.Trips;

public sealed class GetForwardingOptionsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ExcludesTripWithoutAssignedAssistant()
    {
        var withAssistant = Guid.NewGuid();
        var withoutAssistant = Guid.NewGuid();
        var repository = StubDispatchProxy<ITripRepository>.Create();
        repository.SetResult(
            nameof(ITripRepository.ListForwardingCandidatesAsync),
            new ForwardingTripCandidate[]
            {
                Candidate(withAssistant),
                Candidate(withoutAssistant),
            });
        repository.SetResult(
            nameof(ITripRepository.ListSummariesByIdsAsync),
            new InternalTripSummaryDto[]
            {
                Summary(withAssistant, Guid.NewGuid()),
                Summary(withoutAssistant, null),
            });

        var result = await new GetForwardingOptionsQueryHandler(repository.Object).Handle(
            new GetForwardingOptionsQuery(
                Guid.NewGuid(),
                null,
                "ROUTE_STOP",
                Guid.NewGuid(),
                "ROUTE_STOP",
                Guid.NewGuid(),
                10m,
                0.5m,
                DateTimeOffset.UtcNow,
                20),
            CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Trip.TripId.Should().Be(withAssistant);
    }

    private static ForwardingTripCandidate Candidate(Guid tripId)
        => new(
            tripId,
            "Pickup",
            "Target",
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(4),
            true);

    private static InternalTripSummaryDto Summary(Guid tripId, Guid? assistantUserId)
        => new(
            tripId,
            "SCHEDULED",
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(4),
            new InternalTripRouteSummaryDto(Guid.NewGuid(), "Route", "Origin", "Destination"),
            new InternalTripVehicleSummaryDto(
                Guid.NewGuid(),
                "51B-12345",
                "ACTIVE",
                new InternalTripVehicleTypeSummaryDto("BUS", "Bus")),
            Guid.NewGuid(),
            assistantUserId);
}
