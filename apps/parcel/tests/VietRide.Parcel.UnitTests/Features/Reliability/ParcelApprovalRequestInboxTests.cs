using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Reliability.ApprovalRequests;
using VietRide.Parcel.Domain.Entities;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class ParcelApprovalRequestInboxTests
{
    [Fact]
    public async Task CrewChanged_ToNewDriver_RetargetsPendingRequestWithoutDuplicatingIt()
    {
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var request = CreateDepartureRequest(tripId, Guid.NewGuid(), operatorId);
        var custody = Substitute.For<IParcelCustodyExceptionRequestRepository>();
        var departures = Substitute.For<IParcelStopDepartureApprovalRepository>();
        departures.ListPendingByTripForUpdateAsync(tripId, null, Arg.Any<CancellationToken>())
            .Returns([request]);
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var newDriverId = Guid.NewGuid();

        var changed = await new HandleTripCrewChangedCommandHandler(
                custody,
                departures,
                outbox,
                clock)
            .Handle(
                new HandleTripCrewChangedCommand(
                    tripId,
                    operatorId,
                    Guid.NewGuid(),
                    newDriverId),
                CancellationToken.None);

        changed.Should().Be(1);
        await departures.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await outbox.Received(1).EnqueueAsync(
            Arg.Any<Guid>(),
            "parcel.approval.requested",
            Arg.Is<string>(payload => payload.Contains(request.Id.ToString(), StringComparison.OrdinalIgnoreCase)
                && payload.Contains(newDriverId.ToString(), StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CrewChanged_WhenDriverIsUnchanged_DoesNotInvalidateRequests()
    {
        var custody = Substitute.For<IParcelCustodyExceptionRequestRepository>();
        var departures = Substitute.For<IParcelStopDepartureApprovalRepository>();
        var driverId = Guid.NewGuid();
        var handler = new HandleTripCrewChangedCommandHandler(
            custody,
            departures,
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IClock>());

        var changed = await handler.Handle(
            new HandleTripCrewChangedCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                driverId,
                driverId),
            CancellationToken.None);

        changed.Should().Be(0);
        await custody.DidNotReceiveWithAnyArgs()
            .ListPendingByTripForUpdateAsync(default, default);
        await departures.DidNotReceiveWithAnyArgs()
            .ListPendingByTripForUpdateAsync(default, default, default);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyRequestsForTheCurrentlyAssignedDriver()
    {
        var operatorId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var assignedTripId = Guid.NewGuid();
        var foreignTripId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var assigned = CreateDepartureRequest(assignedTripId, stopId, operatorId);
        var foreign = CreateDepartureRequest(foreignTripId, Guid.NewGuid(), operatorId);
        var custody = Substitute.For<IParcelCustodyExceptionRequestRepository>();
        var parcels = Substitute.For<IParcelRepository>();
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        var departures = Substitute.For<IParcelStopDepartureApprovalRepository>();
        departures.ListPendingByOperatorAsync(operatorId, Arg.Any<CancellationToken>())
            .Returns([assigned, foreign]);
        var trips = Substitute.For<ITripServiceClient>();
        trips.GetTripSummariesAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2),
                Arg.Any<CancellationToken>())
            .Returns(TripSummaryBatchOutcome.Success([
                CreateTrip(assignedTripId, driverId, "IN_PROGRESS"),
                CreateTrip(foreignTripId, Guid.NewGuid(), "IN_PROGRESS"),
            ]));

        var result = await new ListParcelApprovalRequestsQueryHandler(
                custody,
                departures,
                trips,
                parcels,
                reliability)
            .Handle(
                new ListParcelApprovalRequestsQuery(
                    driverId,
                    operatorId,
                    "STOP_DEPARTURE",
                    "PENDING_APPROVAL",
                    1,
                    20),
                CancellationToken.None);

        var item = result.Items.Should().ContainSingle().Which;
        item.RequestId.Should().Be(assigned.Id);
        item.RequestType.Should().Be("STOP_DEPARTURE");
        item.StopId.Should().Be(stopId);
        item.ExpiresAt.Should().BeNull();
        item.AvailableActions.Should().Equal("APPROVE", "REJECT");
    }

    private static ParcelStopDepartureApprovalRequest CreateDepartureRequest(
        Guid tripId,
        Guid stopId,
        Guid operatorId)
        => ParcelStopDepartureApprovalRequest.Create(
            tripId,
            stopId,
            operatorId,
            $"[\"{Guid.NewGuid():D}\"]",
            "Operational exception.",
            Guid.NewGuid(),
            "ASSISTANT",
            DateTimeOffset.UtcNow,
            Guid.NewGuid());

    private static TripSummarySnapshot CreateTrip(Guid tripId, Guid driverId, string status)
        => new(
            tripId,
            status,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            new TripRouteSummarySnapshot(Guid.NewGuid(), "Route", "A", "B"),
            new TripVehicleSummarySnapshot(Guid.NewGuid(), "51A-00000", "ACTIVE"))
        {
            DriverUserId = driverId,
        };
}
