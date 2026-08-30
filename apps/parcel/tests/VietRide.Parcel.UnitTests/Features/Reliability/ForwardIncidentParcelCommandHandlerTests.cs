using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class ForwardIncidentParcelCommandHandlerTests
{
    [Fact]
    public async Task Handle_FoundSameTenantParcel_CreatesPlannedTargetLeg()
    {
        var now = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero);
        var operatorId = Guid.NewGuid();
        var sourceTripId = Guid.NewGuid();
        var targetTripId = Guid.NewGuid();
        var dropoffStopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var parcel = CreateParcel(operatorId, sourceTripId, dropoffStopId);
        var incident = ParcelIncident.Open(
            parcel.Id,
            operatorId,
            ParcelIncidentType.WRONG_STOP,
            now.AddHours(72),
            sourceTripId,
            null,
            actorId,
            "OPERATOR_STAFF",
            $"STOP:{dropoffStopId:D}",
            "Wrong station",
            "Found at wrong station",
            null,
            true);
        incident.MarkFound("Station confirmed custody");
        var oldLeg = ParcelTransitLeg.Create(
            parcel.Id,
            sourceTripId,
            operatorId,
            1,
            null,
            dropoffStopId,
            "Origin",
            "Expected stop",
            Guid.NewGuid(),
            "51B-12345");
        oldLeg.Start(now.AddHours(-2));

        var parcels = Substitute.For<IParcelRepository>();
        parcels.GetByIdAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(parcel);
        parcels.TryRequestReliabilityForwardingAsync(
                parcel.Id,
                operatorId,
                targetTripId,
                now,
                Arg.Any<CancellationToken>())
            .Returns(new ParcelPaymentTransitionSnapshot(
                parcel.Id,
                parcel.ParcelCode,
                ParcelStatus.PENDING_TRANSFER_CONFIRM,
                0,
                0,
                operatorId,
                sourceTripId,
                parcel.BookingId,
                parcel.SenderUserId,
                parcel.SizeCategory,
                null));
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.GetIncidentAsync(incident.Id, Arg.Any<CancellationToken>()).Returns(incident);
        reliability.GetTransitLegAsync(parcel.Id, targetTripId, Arg.Any<CancellationToken>())
            .Returns((ParcelTransitLeg?)null);
        reliability.GetLatestTransitLegAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(oldLeg);
        reliability.GetCurrentCustodyAsync(parcel.Id, Arg.Any<CancellationToken>())
            .Returns((ParcelCurrentCustody?)null);
        var trips = Substitute.For<ITripServiceClient>();
        trips.GetTripParcelSnapshotAsync(targetTripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(
                TripSnapshotOutcomeKind.Success,
                CreateTripSnapshot(targetTripId, operatorId),
                null));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        var result = await new ForwardIncidentParcelCommandHandler(
                parcels,
                reliability,
                trips,
                Substitute.For<IIntegrationEventOutbox>(),
                clock)
            .Handle(
                new ForwardIncidentParcelCommand(incident.Id, operatorId, actorId, targetTripId),
                CancellationToken.None);

        result.Status.Should().Be("FORWARDING");
        await reliability.Received(1).AddTransitLegAsync(
            Arg.Is<ParcelTransitLeg>(leg =>
                leg.ParcelId == parcel.Id
                && leg.TripId == targetTripId
                && leg.Sequence == 2
                && leg.Status == ParcelTransitLegStatus.PLANNED
                && leg.ExpectedDestinationId == dropoffStopId),
            Arg.Any<CancellationToken>());
    }

    private static ParcelEntity CreateParcel(Guid operatorId, Guid tripId, Guid dropoffStopId)
        => ParcelEntity.CreatePendingPayment(
            "VR-FORWARD-001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("0900000000"),
            null,
            operatorId,
            tripId,
            dropoffStopId,
            null,
            "Package",
            null,
            ParcelSizeCategory.SMALL,
            2m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));

    private static TripParcelSnapshot CreateTripSnapshot(Guid tripId, Guid operatorId)
    {
        var origin = new TripStationDto(Guid.NewGuid(), "Origin");
        var destination = new TripStationDto(Guid.NewGuid(), "Destination");
        return new TripParcelSnapshot(
            tripId,
            operatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "BOARDING",
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(5),
            100_000,
            origin,
            destination,
            [],
            new TripSeatSummaryDto(40, 10),
            null,
            AssistantUserId: Guid.NewGuid());
    }
}
