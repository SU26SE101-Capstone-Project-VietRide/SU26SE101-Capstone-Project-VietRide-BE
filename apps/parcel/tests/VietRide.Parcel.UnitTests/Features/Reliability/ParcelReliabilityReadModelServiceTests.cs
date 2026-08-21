using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Services;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class ParcelReliabilityReadModelServiceTests
{
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();

    [Fact]
    public async Task BuildAsync_OneHundredParcels_UsesOneBatchPerUpstreamService()
    {
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        var trips = Substitute.For<ITripServiceClient>();
        var identity = Substitute.For<IIdentityServiceClient>();
        var parcels = Enumerable.Range(1, 100).Select(CreateParcel).ToArray();
        var parcelIds = parcels.Select(parcel => parcel.Id).ToHashSet();

        reliability.ListCurrentCustodiesAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 100 && ids.All(parcelIds.Contains)),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelCurrentCustody>());
        reliability.ListActiveIncidentsByParcelsAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 100 && ids.All(parcelIds.Contains)),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelIncident>());
        trips.GetTripSummariesAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { TripId })),
                Arg.Any<CancellationToken>())
            .Returns(TripSummaryBatchOutcome.Success([CreateTripSummary()]));
        identity.GetOperatorsAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { OperatorId })),
                Arg.Any<CancellationToken>())
            .Returns(IdentityOperatorBatchOutcome.Success(
                [new IdentityOperatorSummary(OperatorId, "VietRide Express", null, "0900000000")]));

        var result = await new ParcelReliabilityReadModelService(reliability, trips, identity)
            .BuildAsync(parcels, parcels[0].SenderUserId, includeClaim: false);

        result.Should().HaveCount(100);
        await trips.Received(1).GetTripSummariesAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
        await identity.Received(1).GetOperatorsAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
        await reliability.Received(1).ListCurrentCustodiesAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
        await reliability.Received(1).ListActiveIncidentsByParcelsAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
        await reliability.DidNotReceive().ListLatestClaimsByParcelsAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_RecipientViewer_DoesNotExposeSenderClaim()
    {
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        var trips = Substitute.For<ITripServiceClient>();
        var identity = Substitute.For<IIdentityServiceClient>();
        var parcel = CreateParcel(1);
        var claim = ParcelClaim.Submit(
            parcel.Id,
            Guid.NewGuid(),
            OperatorId,
            parcel.SenderUserId,
            parcel.DeclaredValueVnd,
            1,
            50,
            30_000_000,
            4);

        reliability.ListCurrentCustodiesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelCurrentCustody>());
        reliability.ListActiveIncidentsByParcelsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelIncident>());
        reliability.ListLatestClaimsByParcelsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([claim]);
        trips.GetTripSummariesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(TripSummaryBatchOutcome.Success([CreateTripSummary()]));
        identity.GetOperatorsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(IdentityOperatorBatchOutcome.Success([]));

        var result = await new ParcelReliabilityReadModelService(reliability, trips, identity)
            .BuildAsync([parcel], parcel.RecipientUserId, includeClaim: true);

        result[parcel.Id].Reliability.Claim.Should().BeNull();
        result[parcel.Id].Reliability.AvailableActions.Should().NotContain("SUBMIT_CLAIM");
    }

    private static ParcelEntity CreateParcel(int sequence)
        => ParcelEntity.CreatePendingPayment(
            $"VR-BATCH-{sequence:000}",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("0900000000"),
            null,
            OperatorId,
            TripId,
            Guid.NewGuid(),
            null,
            "Package",
            null,
            ParcelSizeCategory.SMALL,
            2m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));

    private static TripSummarySnapshot CreateTripSummary()
        => new(
            TripId,
            "BOARDING",
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(5),
            new TripRouteSummarySnapshot(Guid.NewGuid(), "Sai Gon - Da Lat", "Sai Gon", "Da Lat"),
            new TripVehicleSummarySnapshot(Guid.NewGuid(), "51B-12345", "ACTIVE"));
}
