using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.OperatorDetail;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Parcels.OperatorDetail;

public sealed class GetOperatorParcelDetailQueryHandlerTests
{
    private static readonly Guid OperatorId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsListProjectionCanonicalDetailFullContactsAndOrderedHistory()
    {
        var parcel = CreateParcel();
        var history = new[]
        {
            CreateHistory(parcel.Id, ParcelStatus.CHECKED_IN, DateTimeOffset.UtcNow.AddMinutes(2), "USER"),
            CreateHistory(parcel.Id, ParcelStatus.PENDING_PAYMENT, DateTimeOffset.UtcNow, "SYSTEM"),
        };
        var repository = Substitute.For<IParcelRepository>();
        repository.GetOperatorDetailAsync(parcel.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new OperatorParcelDetailData(parcel, history));
        var trip = SuccessfulTripClient(parcel);
        var identity = SuccessfulIdentityClient(parcel.SenderUserId);

        var result = await new GetOperatorParcelDetailQueryHandler(repository, trip, identity)
            .Handle(new GetOperatorParcelDetailQuery(parcel.Id, OperatorId), CancellationToken.None);

        result.ParcelId.Should().Be(parcel.Id);
        result.Trip.Status.Should().Be("BOARDING");
        result.Route!.RouteName.Should().Be("Snapshot Route");
        result.Sender.DisplayName.Should().Be("Sender Name");
        result.SenderEmail.Should().Be("sender@example.test");
        result.RecipientEmail.Should().Be("recipient@example.test");
        result.VoucherCode.Should().Be("UI14");
        result.EstimatedGrossPriceVnd.Should().Be(100_000);
        result.FinalGrossPriceVnd.Should().Be(120_000);
        result.LoadedAt.Should().Be(parcel.LoadedAt);
        result.StatusHistory.Select(item => item.Status).Should().Equal(
            ParcelStatus.PENDING_PAYMENT.ToString(),
            ParcelStatus.CHECKED_IN.ToString());
        var json = JsonSerializer.SerializeToElement(
            result,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.GetProperty("parcelId").GetGuid().Should().Be(parcel.Id);
        json.GetProperty("statusHistory").GetArrayLength().Should().Be(2);
        json.TryGetProperty("projection", out _).Should().BeFalse();

        await trip.Received(1).GetTripSummariesAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { parcel.TripId })),
            Arg.Any<CancellationToken>());
        await identity.Received(1).GetUsersAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { parcel.SenderUserId })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MissingOrCrossTenantParcelReturnsMaskedNotFoundWithoutUpstreams()
    {
        var repository = Substitute.For<IParcelRepository>();
        repository.GetOperatorDetailAsync(Arg.Any<Guid>(), OperatorId, Arg.Any<CancellationToken>())
            .Returns((OperatorParcelDetailData?)null);
        var trip = Substitute.For<ITripServiceClient>();
        var identity = Substitute.For<IIdentityServiceClient>();

        var action = () => new GetOperatorParcelDetailQueryHandler(repository, trip, identity)
            .Handle(new GetOperatorParcelDetailQuery(Guid.NewGuid(), OperatorId), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("PARCEL_NOT_FOUND");
        await trip.DidNotReceiveWithAnyArgs().GetTripSummariesAsync(default!, default);
        await identity.DidNotReceiveWithAnyArgs().GetUsersAsync(default!, default);
    }

    [Fact]
    public async Task Handle_TripFailureFailsClosedBeforeIdentityLookup()
    {
        var parcel = CreateParcel();
        var repository = RepositoryWith(parcel);
        var trip = Substitute.For<ITripServiceClient>();
        trip.GetTripSummariesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(TripSummaryBatchOutcome.TransportFailure("unavailable"));
        var identity = Substitute.For<IIdentityServiceClient>();

        var action = () => new GetOperatorParcelDetailQueryHandler(repository, trip, identity)
            .Handle(new GetOperatorParcelDetailQuery(parcel.Id, OperatorId), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ParcelDependencyUnavailableException>();
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
        await identity.DidNotReceiveWithAnyArgs().GetUsersAsync(default!, default);
    }

    [Fact]
    public async Task Handle_IdentityFailureFailsClosed()
    {
        var parcel = CreateParcel();
        var repository = RepositoryWith(parcel);
        var identity = Substitute.For<IIdentityServiceClient>();
        identity.GetUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(IdentityUserBatchOutcome.TransportFailure("unavailable"));

        var action = () => new GetOperatorParcelDetailQueryHandler(
                repository,
                SuccessfulTripClient(parcel),
                identity)
            .Handle(new GetOperatorParcelDetailQuery(parcel.Id, OperatorId), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ParcelDependencyUnavailableException>();
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
    }

    private static IParcelRepository RepositoryWith(ParcelEntity parcel)
    {
        var repository = Substitute.For<IParcelRepository>();
        repository.GetOperatorDetailAsync(parcel.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new OperatorParcelDetailData(parcel, Array.Empty<ParcelStatusHistory>()));
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
                    "BOARDING",
                    DateTimeOffset.UtcNow.AddHours(1),
                    DateTimeOffset.UtcNow.AddHours(6),
                    new TripRouteSummarySnapshot(
                        parcel.TripSnapshotRouteId!.Value,
                        "Current Route",
                        "Current Origin",
                        "Current Destination"),
                    new TripVehicleSummarySnapshot(
                        parcel.TripSnapshotVehicleId!.Value,
                        "51A-99999",
                        "ACTIVE")),
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

    private static ParcelEntity CreateParcel()
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-UI14-DETAIL",
            Guid.NewGuid(),
            null,
            "Recipient Name",
            PhoneNumber.Normalize("+84987654321"),
            "recipient@example.test",
            OperatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Fragile",
            "https://storage.googleapis.com/vietride.appspot.com/parcels/ui14.webp",
            ParcelSizeCategory.MEDIUM,
            10m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(20_000),
            voucherCode: "UI14");
        parcel.ConfigureSettlementV2(
            ParcelSizeCategory.MEDIUM,
            Money.FromRaw(100_000),
            Money.FromRaw(10_000),
            Money.FromRaw(90_000),
            20m,
            Money.FromRaw(18_000),
            Money.FromRaw(5_000),
            Money.FromRaw(50_000),
            6000m,
            DateTimeOffset.UtcNow.AddHours(2),
            DateTimeOffset.UtcNow.AddHours(1));
        parcel.CaptureTripDisplaySnapshot(
            Guid.NewGuid(),
            "Snapshot Route",
            "Snapshot Origin",
            "Snapshot Destination",
            Guid.NewGuid(),
            "51S-14141");
        SetPrivateProperty(parcel, nameof(ParcelEntity.FinalGrossPriceVnd), Money.FromRaw(120_000));
        SetPrivateProperty(parcel, nameof(ParcelEntity.LoadedAt), DateTimeOffset.UtcNow);
        return parcel;
    }

    private static ParcelStatusHistory CreateHistory(
        Guid parcelId,
        ParcelStatus status,
        DateTimeOffset occurredAt,
        string actorType)
    {
        var history = (ParcelStatusHistory)Activator.CreateInstance(
            typeof(ParcelStatusHistory),
            nonPublic: true)!;
        SetPrivateProperty(history, nameof(ParcelStatusHistory.Id), Guid.NewGuid());
        SetPrivateProperty(history, nameof(ParcelStatusHistory.ParcelId), parcelId);
        SetPrivateProperty(history, nameof(ParcelStatusHistory.Status), status);
        SetPrivateProperty(history, nameof(ParcelStatusHistory.OccurredAt), occurredAt);
        SetPrivateProperty(history, nameof(ParcelStatusHistory.ActorType), actorType);
        SetPrivateProperty(history, nameof(ParcelStatusHistory.Source), "STATUS_TRIGGER");
        return history;
    }

    private static void SetPrivateProperty<T>(object target, string propertyName, T value)
        => target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(target, value);
}
