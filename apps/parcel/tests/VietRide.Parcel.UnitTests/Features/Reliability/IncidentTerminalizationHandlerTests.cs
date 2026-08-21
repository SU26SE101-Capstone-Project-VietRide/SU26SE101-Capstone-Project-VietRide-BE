using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class IncidentTerminalizationHandlerTests
{
    [Fact]
    public async Task MarkFound_CancelsOnlyOutstandingSearchTasks()
    {
        var now = new DateTimeOffset(2026, 8, 21, 11, 0, 0, TimeSpan.Zero);
        var parcel = CreateParcel();
        var incident = CreateSearchingIncident(parcel, now.AddHours(72));
        var open = ParcelSearchTask.Create(
            incident.Id, parcel.Id, ParcelSearchTaskType.VEHICLE_SWEEP, null, null, now.AddMinutes(30));
        var inProgress = ParcelSearchTask.Create(
            incident.Id, parcel.Id, ParcelSearchTaskType.STATION_INVENTORY, null, null, now.AddHours(2));
        inProgress.Start();
        var completed = ParcelSearchTask.Create(
            incident.Id, parcel.Id, ParcelSearchTaskType.CREW_CONFIRMATION, null, null, now.AddHours(2));
        completed.Complete("Crew checked the vehicle.", null, now.AddMinutes(-5));
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.GetIncidentAsync(incident.Id, Arg.Any<CancellationToken>()).Returns(incident);
        reliability.ListSearchTasksAsync(incident.Id, Arg.Any<CancellationToken>())
            .Returns([open, inProgress, completed]);
        var parcels = Substitute.For<IParcelRepository>();
        parcels.GetByIdAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(parcel);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        var response = await new MarkIncidentFoundCommandHandler(
                reliability,
                parcels,
                Substitute.For<IParcelCustodyService>(),
                Substitute.For<IIntegrationEventOutbox>(),
                clock)
            .Handle(
                new MarkIncidentFoundCommand(
                    incident.Id,
                    parcel.OperatorId,
                    Guid.NewGuid(),
                    "VEHICLE",
                    null,
                    "Vehicle cargo hold",
                    null,
                    "Found during vehicle sweep."),
                CancellationToken.None);

        response.Status.Should().Be("FOUND");
        open.Status.Should().Be(ParcelSearchTaskStatus.CANCELLED);
        inProgress.Status.Should().Be(ParcelSearchTaskStatus.CANCELLED);
        completed.Status.Should().Be(ParcelSearchTaskStatus.COMPLETED);
        open.CompletedAt.Should().Be(now);
        inProgress.CompletedAt.Should().Be(now);
        await reliability.Received(2).UpdateSearchTaskAsync(
            Arg.Any<ParcelSearchTask>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeclareLost_FailsOutstandingTasksAndMarksActiveLegLost()
    {
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var parcel = CreateParcel();
        var incident = CreateSearchingIncident(parcel, now.AddMinutes(-1));
        var open = ParcelSearchTask.Create(
            incident.Id, parcel.Id, ParcelSearchTaskType.VEHICLE_SWEEP, null, null, now.AddHours(-71));
        var inProgress = ParcelSearchTask.Create(
            incident.Id, parcel.Id, ParcelSearchTaskType.STATION_INVENTORY, null, null, now.AddHours(-70));
        inProgress.Start();
        var completed = ParcelSearchTask.Create(
            incident.Id, parcel.Id, ParcelSearchTaskType.CREW_CONFIRMATION, null, null, now.AddHours(-70));
        completed.Complete("Crew result retained.", "[\"photo\"]", now.AddHours(-69));
        var leg = ParcelTransitLeg.Create(
            parcel.Id,
            parcel.TripId,
            parcel.OperatorId,
            1,
            null,
            parcel.DropoffStopId,
            "Origin",
            "Destination",
            Guid.NewGuid(),
            "51B-12345");
        leg.Start(now.AddHours(-73));
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.GetIncidentAsync(incident.Id, Arg.Any<CancellationToken>()).Returns(incident);
        reliability.ListSearchTasksAsync(incident.Id, Arg.Any<CancellationToken>())
            .Returns([open, inProgress, completed]);
        reliability.GetActiveLegAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(leg);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        var response = await new DeclareIncidentLostCommandHandler(
                reliability,
                Substitute.For<IIntegrationEventOutbox>(),
                clock)
            .Handle(
                new DeclareIncidentLostCommand(
                    incident.Id,
                    parcel.OperatorId,
                    Guid.NewGuid(),
                    "Search completed without recovery."),
                CancellationToken.None);

        response.Status.Should().Be("LOST_CONFIRMED");
        open.Status.Should().Be(ParcelSearchTaskStatus.FAILED);
        inProgress.Status.Should().Be(ParcelSearchTaskStatus.FAILED);
        completed.Status.Should().Be(ParcelSearchTaskStatus.COMPLETED);
        completed.Result.Should().Be("Crew result retained.");
        leg.Status.Should().Be(ParcelTransitLegStatus.LOST);
        leg.EndedAt.Should().Be(now);
        await reliability.Received(2).UpdateSearchTaskAsync(
            Arg.Any<ParcelSearchTask>(),
            Arg.Any<CancellationToken>());
        await reliability.Received(1).UpdateTransitLegAsync(leg, Arg.Any<CancellationToken>());
    }

    private static ParcelIncident CreateSearchingIncident(ParcelEntity parcel, DateTimeOffset deadline)
    {
        var incident = ParcelIncident.Open(
            parcel.Id,
            parcel.OperatorId,
            ParcelIncidentType.MISSING,
            deadline,
            parcel.TripId,
            null,
            null,
            "SYSTEM",
            "Expected destination",
            "Vehicle",
            "Manifest reconciliation gap",
            null,
            operatorProcessBreach: true);
        incident.StartSearch();
        return incident;
    }

    private static ParcelEntity CreateParcel()
        => ParcelEntity.CreatePendingPayment(
            "VR-INCIDENT-001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("0900000000"),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Package",
            null,
            ParcelSizeCategory.SMALL,
            2m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));
}
