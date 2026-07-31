using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.OperatorList;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Parcels.OperatorList;

public sealed class GetOperatorParcelsQueryHandlerTests
{
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsOnlyRepositoryPageForAuthenticatedOperatorScope()
    {
        var parcel = CreateParcel();
        var repository = Substitute.For<IParcelRepository>();
        repository.ListByOperatorAsync(
                OperatorId,
                ParcelStatus.PENDING_OPERATOR_REVIEW,
                TripId,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));
        var trip = SuccessfulTripClient(parcel);
        var identity = SuccessfulIdentityClient(parcel.SenderUserId);

        var result = await new GetOperatorParcelsQueryHandler(repository, trip, identity).Handle(
            new GetOperatorParcelsQuery(
                OperatorId,
                "pending_operator_review",
                TripId,
                null,
                1,
                20),
            CancellationToken.None);

        result.Items.Should().ContainSingle();
        var item = result.Items[0];
        item.ParcelId.Should().Be(parcel.Id);
        item.ParcelCode.Should().Be(parcel.ParcelCode);
        item.Status.Should().Be("PENDING_OPERATOR_REVIEW");
        item.TripId.Should().Be(TripId);
        item.SenderUserId.Should().Be(parcel.SenderUserId);
        item.RecipientName.Should().Be(parcel.RecipientName);
        item.EstimatedSizeCategory.Should().Be("EXTRA_LARGE");
        item.EstimatedChargeableWeightKg.Should().Be(parcel.EstimatedChargeableWeightKg);
        item.DepositRequiredVnd.Should().Be(parcel.DepositRequiredVnd.Amount);
        item.PendingActionType.Should().BeNull();
        item.PhotoUrl.Should().Be(parcel.PhotoUrl);
        item.Trip.Should().NotBeNull();
        item.Trip!.TripId.Should().Be(parcel.TripId);
        item.Trip.Status.Should().Be("SCHEDULED");
        item.Trip.Vehicle.Should().Be(new OperatorParcelVehicleResponse(VehicleId, "51C-12345"));
        item.Route.Should().NotBeNull();
        item.Route!.RouteName.Should().Be("Current Route");
        item.Sender.Should().Be(new OperatorParcelUserResponse(
            parcel.SenderUserId,
            "Sender Name",
            "+84901234567"));
        item.Recipient.Should().Be(new OperatorParcelUserResponse(
            parcel.RecipientUserId,
            parcel.RecipientName,
            parcel.RecipientPhone.ToString()));
        item.SizeCategory.Should().Be("EXTRA_LARGE");
        item.Description.Should().Be(parcel.Description);
        item.EstimatedWeightKg.Should().Be(parcel.EstimatedWeightKg);
        item.EstimatedVolumeM3.Should().Be(parcel.EstimatedVolumeM3);
        item.EstimatedTotalPriceVnd.Should().Be(parcel.EstimatedTotalPriceVnd.Amount);
        item.UpdatedAt.Should().Be(parcel.UpdatedAt);

        await repository.Received(1).ListByOperatorAsync(
            OperatorId,
            ParcelStatus.PENDING_OPERATOR_REVIEW,
            TripId,
            null,
            1,
            20,
            Arg.Any<CancellationToken>());
        await trip.Received(1).GetTripSummariesAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { parcel.TripId })),
            Arg.Any<CancellationToken>());
        await identity.Received(1).GetUsersAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { parcel.SenderUserId })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CompleteSnapshotWinsAsOneImmutableRouteTuple()
    {
        var parcel = CreateParcel();
        parcel.CaptureTripDisplaySnapshot(
            Guid.NewGuid(),
            "Snapshot Route",
            "Snapshot Origin",
            "Snapshot Destination",
            Guid.NewGuid(),
            "51S-11111");
        var repository = RepositoryWith(parcel);

        var result = await new GetOperatorParcelsQueryHandler(
            repository,
            SuccessfulTripClient(parcel),
            SuccessfulIdentityClient(parcel.SenderUserId)).Handle(Query(), CancellationToken.None);

        result.Items.Single().Route.Should().Be(new OperatorParcelRouteResponse(
            parcel.TripSnapshotRouteId!.Value,
            "Snapshot Route",
            "Snapshot Origin",
            "Snapshot Destination"));
        result.Items.Single().Trip!.Vehicle.Should().Be(new OperatorParcelVehicleResponse(
            parcel.TripSnapshotVehicleId!.Value,
            "51S-11111"));
    }

    [Fact]
    public async Task Handle_PartialLegacySnapshotUsesEntireTripFallbackWithoutMixing()
    {
        var parcel = CreateParcel();
        SetPrivateProperty(parcel, nameof(ParcelEntity.TripSnapshotRouteName), "Stale Partial Route");
        var repository = RepositoryWith(parcel);

        var result = await new GetOperatorParcelsQueryHandler(
            repository,
            SuccessfulTripClient(parcel),
            SuccessfulIdentityClient(parcel.SenderUserId)).Handle(Query(), CancellationToken.None);

        result.Items.Single().Route.Should().Be(new OperatorParcelRouteResponse(
            RouteId,
            "Current Route",
            "Current Origin",
            "Current Destination"));
        result.Items.Single().Trip!.Vehicle.Should().Be(new OperatorParcelVehicleResponse(
            VehicleId,
            "51C-12345"));
    }

    [Fact]
    public async Task Handle_EmptyPageDoesNotCallEitherUpstream()
    {
        var repository = Substitute.For<IParcelRepository>();
        repository.ListByOperatorAsync(
                OperatorId,
                null,
                null,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([], 1, 20, 0));
        var trip = Substitute.For<ITripServiceClient>();
        var identity = Substitute.For<IIdentityServiceClient>();

        var result = await new GetOperatorParcelsQueryHandler(repository, trip, identity)
            .Handle(Query(), CancellationToken.None);

        result.Items.Should().BeEmpty();
        await trip.DidNotReceive().GetTripSummariesAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
        await identity.DidNotReceive().GetUsersAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TripTransportFailureFailsClosedBeforeIdentityLookup()
    {
        var parcel = CreateParcel();
        var trip = Substitute.For<ITripServiceClient>();
        trip.GetTripSummariesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(TripSummaryBatchOutcome.TransportFailure("trip unavailable"));
        var identity = Substitute.For<IIdentityServiceClient>();

        var action = () => new GetOperatorParcelsQueryHandler(RepositoryWith(parcel), trip, identity)
            .Handle(Query(), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ParcelDependencyUnavailableException>();
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
        await identity.DidNotReceive().GetUsersAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_IdentityTransportFailureFailsClosed()
    {
        var parcel = CreateParcel();
        var identity = Substitute.For<IIdentityServiceClient>();
        identity.GetUsersAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(IdentityUserBatchOutcome.TransportFailure("identity unavailable"));

        var action = () => new GetOperatorParcelsQueryHandler(
            RepositoryWith(parcel),
            SuccessfulTripClient(parcel),
            identity).Handle(Query(), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ParcelDependencyUnavailableException>();
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
    }

    [Theory]
    [InlineData("UNKNOWN", null, 1, 20)]
    [InlineData("999", null, 1, 20)]
    [InlineData(null, "UNKNOWN", 1, 20)]
    [InlineData(null, "999", 1, 20)]
    [InlineData(null, null, 0, 20)]
    [InlineData(null, null, 1, 0)]
    [InlineData(null, null, 1, 101)]
    public void Validator_RejectsInvalidFiltersOrPagination(
        string? status,
        string? pendingActionType,
        int page,
        int pageSize)
    {
        var result = new GetOperatorParcelsQueryValidator().Validate(
            new GetOperatorParcelsQuery(
                OperatorId,
                status,
                null,
                pendingActionType,
                page,
                pageSize));

        result.IsValid.Should().BeFalse();
    }

    private static ParcelEntity CreateParcel()
    {
        var parcel = ParcelEntity.CreatePendingOperatorReview(
            "VR-PCL-OPERATOR-001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Nguyen Van A",
            PhoneNumber.Normalize("0900000000"),
            null,
            OperatorId,
            TripId,
            null,
            null,
            "Hang can duyet",
            "https://storage.googleapis.com/vietride.appspot.com/parcels/photo.webp",
            ParcelSizeCategory.EXTRA_LARGE,
            50m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(10_000));
        parcel.ConfigureSettlementV2(
            ParcelSizeCategory.EXTRA_LARGE,
            Money.FromRaw(50_000),
            Money.Zero,
            Money.FromRaw(50_000),
            20m,
            Money.FromRaw(10_000),
            Money.FromRaw(1_000),
            Money.FromRaw(1_000),
            6000m,
            DateTimeOffset.UtcNow.AddHours(2),
            DateTimeOffset.UtcNow.AddHours(1));
        return parcel;
    }

    private static readonly Guid RouteId = Guid.NewGuid();
    private static readonly Guid VehicleId = Guid.NewGuid();

    private static IParcelRepository RepositoryWith(ParcelEntity parcel)
    {
        var repository = Substitute.For<IParcelRepository>();
        repository.ListByOperatorAsync(
                OperatorId,
                null,
                null,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));
        return repository;
    }

    private static ITripServiceClient SuccessfulTripClient(ParcelEntity parcel)
    {
        var trip = Substitute.For<ITripServiceClient>();
        trip.GetTripSummariesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(TripSummaryBatchOutcome.Success(
            [
                new TripSummarySnapshot(
                    parcel.TripId,
                    "SCHEDULED",
                    DateTimeOffset.UtcNow.AddHours(1),
                    DateTimeOffset.UtcNow.AddHours(9),
                    new TripRouteSummarySnapshot(
                        RouteId,
                        "Current Route",
                        "Current Origin",
                        "Current Destination"),
                    new TripVehicleSummarySnapshot(VehicleId, "51C-12345", "ACTIVE")),
            ]));
        return trip;
    }

    private static IIdentityServiceClient SuccessfulIdentityClient(Guid senderUserId)
    {
        var identity = Substitute.For<IIdentityServiceClient>();
        identity.GetUsersAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(IdentityUserBatchOutcome.Success(
            [
                new IdentityUserSummary(
                    senderUserId,
                    "Sender Name",
                    "+84901234567",
                    "sender@example.test",
                    null,
                    "PASSENGER",
                    null,
                    "ACTIVE",
                    false),
            ]));
        return identity;
    }

    private static GetOperatorParcelsQuery Query()
        => new(OperatorId, null, null, null, 1, 20);

    private static void SetPrivateProperty<T>(ParcelEntity parcel, string propertyName, T value)
        => typeof(ParcelEntity).GetProperty(propertyName)!.SetValue(parcel, value);
}
