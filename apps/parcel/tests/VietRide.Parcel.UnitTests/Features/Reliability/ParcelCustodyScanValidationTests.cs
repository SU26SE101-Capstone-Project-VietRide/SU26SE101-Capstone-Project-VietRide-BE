using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Features.Reliability.CustodyScan;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class ParcelCustodyScanValidationTests
{
    [Fact]
    public async Task ArrivedAtStopScan_WhenStopIsNotOperationalLocation_IsRejectedWithoutCustodyWrite()
    {
        var parcel = CreateParcel();
        var assistantId = Guid.NewGuid();
        var requestedStopId = Guid.NewGuid();
        var actualStopId = Guid.NewGuid();
        var parcels = Substitute.For<IParcelRepository>();
        parcels.GetByIdAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(parcel);
        var custody = Substitute.For<IParcelCustodyService>();
        var trips = Substitute.For<ITripServiceClient>();
        trips.AuthorizeAssistantForTripAsync(
                parcel.TripId,
                assistantId,
                parcel.OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        trips.GetTripParcelSnapshotAsync(parcel.TripId, Arg.Any<CancellationToken>())
            .Returns(SuccessfulTrip(parcel));
        trips.GetTripOperationalLocationAsync(parcel.TripId, Arg.Any<CancellationToken>())
            .Returns(new TripOperationalLocationOutcome(
                TripOperationalLocationOutcomeKind.Success,
                new TripOperationalLocationSnapshot(
                    parcel.TripId,
                    Guid.NewGuid(),
                    "IN_PROGRESS",
                    actualStopId,
                    "ARRIVED",
                    DateTimeOffset.UtcNow,
                    null,
                    null),
                null));

        var action = () => new RecordParcelCustodyScanCommandHandler(parcels, custody, trips)
            .Handle(
                new RecordParcelCustodyScanCommand(
                    parcel.Id,
                    parcel.OperatorId,
                    assistantId,
                    "ASSISTANT",
                    parcel.ParcelCode,
                    "ARRIVED_AT_STOP",
                    "ROUTE_STOP",
                    requestedStopId,
                    "Wrong stop",
                    null,
                    null,
                    Guid.NewGuid(),
                    RequireAssignedCrew: true),
                CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("PARCEL_CUSTODY_LOCATION_MISMATCH");
        await custody.DidNotReceiveWithAnyArgs().AppendAsync(
            default!, default, default, default, default, default, default!, default!, default, default, default, default);
    }

    private static ParcelEntity CreateParcel()
        => ParcelEntity.CreatePendingPayment(
            "VR-CUSTODY-LOCATION-001",
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

    private static TripSnapshotOutcome SuccessfulTrip(ParcelEntity parcel)
        => new(
            TripSnapshotOutcomeKind.Success,
            new TripParcelSnapshot(
                parcel.TripId,
                parcel.OperatorId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "IN_PROGRESS",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(2),
                100_000,
                new TripStationDto(Guid.NewGuid(), "Origin"),
                new TripStationDto(Guid.NewGuid(), "Destination"),
                [],
                new TripSeatSummaryDto(20, 10),
                null,
                null),
            null);
}
