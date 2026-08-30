using System.Reflection;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Reliability.Reconciliation;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class ReconcileParcelStopTests
{
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid StopId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid AssistantId = Guid.NewGuid();
    private static readonly Guid ParcelId = Guid.NewGuid();

    [Fact]
    public async Task Handle_StopAlreadyDeparted_RejectsStaleTripSnapshot()
    {
        var (handler, parcels, _, _) = CreateHandler(atCurrentStop: false);

        var action = () => handler.Handle(
            new ReconcileParcelStopCommand(
                TripId,
                StopId,
                AssistantId,
                OperatorId,
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("PARCEL_CUSTODY_LOCATION_MISMATCH");
        await parcels.DidNotReceive().ListDropoffManifestByTripAndStopAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DerivesScannedCountFromPersistedUnloadCustodyEvent()
    {
        var parcel = CreateManifestParcel(ParcelStatus.UNLOADED);
        var custodyEvent = ParcelCustodyEvent.Create(
            ParcelId,
            null,
            TripId,
            ParcelCustodyEventType.UNLOADED,
            ParcelCustodyLocationType.ROUTE_STOP,
            StopId,
            ParcelCustodyLocationType.ROUTE_STOP,
            StopId,
            "Stop B",
            null,
            AssistantId,
            "ASSISTANT",
            DateTimeOffset.UtcNow,
            "UNLOAD",
            Guid.NewGuid().ToString("D"),
            null,
            null,
            1);
        var (handler, parcels, reliability, _) = CreateHandler();
        parcels.ListDropoffManifestByTripAndStopAsync(TripId, StopId, Arg.Any<CancellationToken>())
            .Returns(new[] { parcel });
        reliability.ListCustodyEventsByParcelsAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { ParcelId })),
                Arg.Any<CancellationToken>())
            .Returns(new[] { custodyEvent });
        reliability.ListActiveIncidentsByParcelsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelIncident>());
        reliability.ListCurrentCustodiesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelCurrentCustody>());

        var result = await handler.Handle(
            new ReconcileParcelStopCommand(
                TripId,
                StopId,
                AssistantId,
                OperatorId,
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        result.ExpectedCount.Should().Be(1);
        result.ScannedCount.Should().Be(1);
        result.UnresolvedParcelIds.Should().BeEmpty();
        result.CanDepart.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NoPersistedCustodyFact_ReturnsParcelAsUnresolved()
    {
        var parcel = CreateManifestParcel(ParcelStatus.IN_TRANSIT);
        var (handler, parcels, reliability, _) = CreateHandler();
        parcels.ListDropoffManifestByTripAndStopAsync(TripId, StopId, Arg.Any<CancellationToken>())
            .Returns(new[] { parcel });
        reliability.ListCustodyEventsByParcelsAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { ParcelId })),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelCustodyEvent>());

        var result = await handler.Handle(
            new ReconcileParcelStopCommand(
                TripId,
                StopId,
                AssistantId,
                OperatorId,
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        result.ScannedCount.Should().Be(0);
        result.UnresolvedParcelIds.Should().ContainSingle().Which.Should().Be(ParcelId);
        result.CanDepart.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UnresolvedWithReason_CreatesPendingApprovalWithoutAuthorizingDeparture()
    {
        var parcel = CreateManifestParcel(ParcelStatus.IN_TRANSIT);
        var (handler, parcels, reliability, approvals) = CreateHandler();
        parcels.ListDropoffManifestByTripAndStopAsync(TripId, StopId, Arg.Any<CancellationToken>())
            .Returns(new[] { parcel });
        reliability.ListCustodyEventsByParcelsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelCustodyEvent>());
        reliability.ListActiveIncidentsByParcelsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelIncident>());
        reliability.ListCurrentCustodiesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelCurrentCustody>());

        var result = await handler.Handle(
            new ReconcileParcelStopCommand(
                TripId,
                StopId,
                AssistantId,
                OperatorId,
                "Vehicle must leave for an emergency.",
                Guid.NewGuid()),
            CancellationToken.None);

        result.CanDepart.Should().BeFalse();
        result.RequiresSupervisorApproval.Should().BeTrue();
        result.DepartureOverrideRequest.Should().NotBeNull();
        result.DepartureOverrideRequest!.Status.Should().Be("PENDING_APPROVAL");
        result.DepartureOverrideRequest.RequestedByUserId.Should().Be(AssistantId);
        result.DepartureOverrideRequest.ReviewedByUserId.Should().BeNull();
        await parcels.Received(1).TrySetPendingOperatorActionAsync(
            ParcelId,
            PendingActionType.CUSTODY_EXCEPTION,
            Arg.Any<string>(),
            null,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>(),
            ParcelStatus.IN_TRANSIT);
        await approvals.Received(1).AddAsync(
            Arg.Is<ParcelStopDepartureApprovalRequest>(request =>
                request.TripId == TripId
                && request.StopId == StopId
                && request.RequestedByUserId == AssistantId),
            Arg.Any<CancellationToken>());
    }

    private static (
        ReconcileParcelStopCommandHandler Handler,
        IParcelRepository Parcels,
        IParcelReliabilityRepository Reliability,
        IParcelStopDepartureApprovalRepository Approvals) CreateHandler(bool atCurrentStop = true)
    {
        var parcels = Substitute.For<IParcelRepository>();
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        var departureApprovals = Substitute.For<IParcelStopDepartureApprovalRepository>();
        var trips = Substitute.For<ITripServiceClient>();
        trips.AuthorizeAssistantForTripAsync(
                TripId,
                AssistantId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        trips.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(
                TripSnapshotOutcomeKind.Success,
                CreateTripSnapshot(),
                null));
        trips.GetTripOperationalLocationAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripOperationalLocationOutcome(
                TripOperationalLocationOutcomeKind.Success,
                new TripOperationalLocationSnapshot(
                    TripId,
                    Guid.NewGuid(),
                    "IN_PROGRESS",
                    atCurrentStop ? StopId : null,
                    atCurrentStop ? "ARRIVED" : null,
                    atCurrentStop ? DateTimeOffset.UtcNow : null,
                    null,
                    null),
                null));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        return (
            new ReconcileParcelStopCommandHandler(
                parcels,
                reliability,
                departureApprovals,
                trips,
                Substitute.For<IIntegrationEventOutbox>(),
                clock),
            parcels,
            reliability,
            departureApprovals);
    }

    private static TripParcelSnapshot CreateTripSnapshot()
    {
        var station = new TripStationDto(Guid.NewGuid(), "Station");
        return new TripParcelSnapshot(
            TripId,
            OperatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "IN_PROGRESS",
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddHours(1),
            100_000,
            station,
            station,
            new[]
            {
                new TripStopDto(
                    StopId,
                    1,
                    false,
                    true,
                    DateTimeOffset.UtcNow,
                    10,
                    null,
                    "ARRIVED",
                    DateTimeOffset.UtcNow,
                    null),
            },
            new TripSeatSummaryDto(40, 20),
            null);
    }

    private static ParcelEntity CreateManifestParcel(ParcelStatus status)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-RECONCILE-001",
            Guid.NewGuid(),
            null,
            "Recipient",
            PhoneNumber.Normalize("0912345678"),
            null,
            OperatorId,
            TripId,
            StopId,
            null,
            "Item",
            null,
            ParcelSizeCategory.MEDIUM,
            5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));
        Set(parcel, nameof(parcel.Id), ParcelId);
        Set(parcel, nameof(parcel.Status), status);
        return parcel;
    }

    private static void Set<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property!.SetValue(target, value);
    }
}
