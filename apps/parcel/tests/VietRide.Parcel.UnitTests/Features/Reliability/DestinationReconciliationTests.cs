using System.Reflection;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Reliability.Reconciliation;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class DestinationReconciliationTests
{
    [Fact]
    public async Task UnresolvedTerminalParcel_OpensUnscannedHandoffAndRequiresDriverCompletion()
    {
        var now = new DateTimeOffset(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);
        var parcel = CreateParcel();
        SetProperty(parcel, nameof(ParcelEntity.Status), ParcelStatus.IN_TRANSIT);
        var assistantId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var parcels = Substitute.For<IParcelRepository>();
        parcels.ListTerminalDropoffManifestByTripAsync(parcel.TripId, Arg.Any<CancellationToken>())
            .Returns([parcel]);
        parcels.TrySetPendingOperatorActionAsync(
                parcel.Id,
                PendingActionType.CUSTODY_EXCEPTION,
                Arg.Any<string>(),
                null,
                now,
                Arg.Any<CancellationToken>(),
                ParcelStatus.IN_TRANSIT)
            .Returns(true);
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.ListCustodyEventsByParcelsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
        reliability.ListActiveIncidentsByParcelsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
        reliability.ListCurrentCustodiesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
        ParcelIncident? addedIncident = null;
        reliability.AddIncidentAsync(
                Arg.Do<ParcelIncident>(incident => addedIncident = incident),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var trips = Substitute.For<ITripServiceClient>();
        trips.AuthorizeAssistantForTripAsync(
                parcel.TripId,
                assistantId,
                parcel.OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        trips.GetTripParcelSnapshotAsync(parcel.TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(
                TripSnapshotOutcomeKind.Success,
                CreateTrip(parcel, destinationId, now),
                null));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        var response = await new ReconcileParcelDestinationCommandHandler(
                parcels,
                reliability,
                trips,
                Substitute.For<IIntegrationEventOutbox>(),
                clock)
            .Handle(
                new ReconcileParcelDestinationCommand(
                    parcel.TripId,
                    assistantId,
                    parcel.OperatorId,
                    Guid.NewGuid()),
                CancellationToken.None);

        response.CanComplete.Should().BeTrue();
        response.CanCompleteTrip.Should().BeTrue();
        response.AllExpectedParcelsDelivered.Should().BeFalse();
        response.RequiresDriverCompletion.Should().BeTrue();
        response.UnresolvedParcels.Should().ContainSingle(item => item.ParcelId == parcel.Id);
        addedIncident.Should().NotBeNull();
        addedIncident!.Type.Should().Be(ParcelIncidentType.UNSCANNED_HANDOFF);
        addedIncident.Status.Should().Be(ParcelIncidentStatus.SEARCHING);
        addedIncident.ExpectedLocation.Should().Be($"DESTINATION_STATION:{destinationId:D}");
        await parcels.Received(1).TrySetPendingOperatorActionAsync(
            parcel.Id,
            PendingActionType.CUSTODY_EXCEPTION,
            Arg.Any<string>(),
            null,
            now,
            Arg.Any<CancellationToken>(),
            ParcelStatus.IN_TRANSIT);
    }

    [Fact]
    public async Task CompletionClearance_RecognizesOnlyDestinationReconciliationIncident()
    {
        var parcel = CreateParcel();
        SetProperty(parcel, nameof(ParcelEntity.Status), ParcelStatus.PENDING_OPERATOR_ACTION);
        SetProperty(parcel, nameof(ParcelEntity.PendingActionType), PendingActionType.CUSTODY_EXCEPTION);
        var parcels = Substitute.For<IParcelRepository>();
        parcels.ListTerminalDropoffManifestByTripAsync(parcel.TripId, Arg.Any<CancellationToken>())
            .Returns([parcel]);
        var incident = ParcelIncident.Open(
            parcel.Id,
            parcel.OperatorId,
            ParcelIncidentType.UNSCANNED_HANDOFF,
            DateTimeOffset.UtcNow.AddHours(72),
            parcel.TripId,
            null,
            Guid.NewGuid(),
            "ASSISTANT",
            $"DESTINATION_STATION:{Guid.NewGuid():D}",
            null,
            "Destination reconciliation",
            null,
            true);
        incident.StartSearch();
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.ListActiveIncidentsByParcelsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([incident]);

        var response = await new GetParcelTripCompletionClearanceQueryHandler(parcels, reliability)
            .Handle(
                new GetParcelTripCompletionClearanceQuery(parcel.TripId, parcel.OperatorId),
                CancellationToken.None);

        response.Status.Should().Be("ACKNOWLEDGED_INCIDENTS");
        response.UnresolvedParcelIds.Should().ContainSingle().Which.Should().Be(parcel.Id);
        response.IncidentIds.Should().ContainSingle().Which.Should().Be(incident.Id);
    }

    private static ParcelEntity CreateParcel()
        => ParcelEntity.CreatePendingPayment(
            "VR-DEST-RECON-001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("0900000000"),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            "Package",
            null,
            ParcelSizeCategory.SMALL,
            2m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));

    private static TripParcelSnapshot CreateTrip(
        ParcelEntity parcel,
        Guid destinationId,
        DateTimeOffset now)
        => new(
            parcel.TripId,
            parcel.OperatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "IN_PROGRESS",
            now.AddHours(-2),
            now,
            100_000,
            new TripStationDto(Guid.NewGuid(), "Origin"),
            new TripStationDto(destinationId, "Destination"),
            [],
            new TripSeatSummaryDto(20, 10),
            null,
            now);

    private static void SetProperty<T>(ParcelEntity parcel, string propertyName, T value)
        => typeof(ParcelEntity)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(parcel, value);
}
