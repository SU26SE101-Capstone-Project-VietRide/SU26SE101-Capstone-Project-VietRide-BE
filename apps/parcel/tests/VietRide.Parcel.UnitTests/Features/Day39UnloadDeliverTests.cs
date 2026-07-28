using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.Deliver;
using VietRide.Parcel.Application.Features.Parcels.Unload;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class Day39UnloadDeliverTests
{
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid SenderUserId = Guid.NewGuid();
    private static readonly Guid RecipientUserId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid AssistantUserId = Guid.NewGuid();
    private static readonly Guid DropoffStopId = Guid.NewGuid();
    private static readonly string DeliveryPhotoUrl =
        $"https://storage.googleapis.com/vietride.appspot.com/parcel-ops/{OperatorId:D}/{AssistantUserId:D}/{ParcelId:D}/delivery.webp";

    [Fact]
    public async Task Unload_UnassignedAssistant_ReturnsForbiddenBeforeTransition()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        tripClient.AuthorizeAssistantForTripAsync(
                TripId,
                AssistantUserId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Denied));

        var handler = new UnloadParcelCommandHandler(
            repository,
            tripClient,
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IUnitOfWork>());

        var action = () => handler.Handle(
            new UnloadParcelCommand(ParcelId, AssistantUserId, OperatorId),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<ForbiddenException>()).Which;
        exception.ErrorCode.Should().Be("FORBIDDEN");
        await repository.DidNotReceive().TryMarkUnloadedAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unload_MissingAssignedTrip_FailsClosedAsForbiddenBeforeTransition()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        tripClient.AuthorizeAssistantForTripAsync(
                TripId,
                AssistantUserId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.TripNotFound));

        var handler = new UnloadParcelCommandHandler(
            repository,
            tripClient,
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IUnitOfWork>());

        var action = () => handler.Handle(
            new UnloadParcelCommand(ParcelId, AssistantUserId, OperatorId),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<ForbiddenException>()).Which;
        exception.ErrorCode.Should().Be("FORBIDDEN");
        await repository.DidNotReceive().TryMarkUnloadedAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await tripClient.DidNotReceive().GetTripParcelSnapshotAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unload_StopBoundBeforeMatchingArrival_ReturnsDropoffStopNotArrived()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = AuthorizedTripClient();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(
                TripSnapshotOutcomeKind.Success,
                TripSnapshot(destinationArrivedAt: DateTimeOffset.UtcNow, stopStatus: "PENDING"),
                null));

        var handler = new UnloadParcelCommandHandler(
            repository,
            tripClient,
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IUnitOfWork>());

        var action = () => handler.Handle(
            new UnloadParcelCommand(ParcelId, AssistantUserId, OperatorId),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedValidationException>()).Which;
        exception.ErrorCode.Should().Be("DROP_OFF_STOP_NOT_ARRIVED");
        await repository.DidNotReceive().TryMarkUnloadedAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unload_ArrivedStop_SetsUnloadedAndEmitsOneEventWithOneCargoRelease()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = AuthorizedTripClient();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryMarkUnloadedAsync(
                ParcelId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.UNLOADED));
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(
                TripSnapshotOutcomeKind.Success,
                TripSnapshot(destinationArrivedAt: null),
                null));
        tripClient.ReleaseCargoAsync(
                TripId,
                ParcelId,
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));

        var handler = new UnloadParcelCommandHandler(
            repository,
            tripClient,
            outbox,
            unitOfWork);

        var response = await handler.Handle(
            new UnloadParcelCommand(ParcelId, AssistantUserId, OperatorId),
            CancellationToken.None);

        response.Status.Should().Be("UNLOADED");
        await outbox.Received(1).EnqueueAsync(
            ParcelOutboxEvents.Unloaded,
            Arg.Is<string>(payload => HasUnloadedPayload(payload)),
            Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().EnqueueAsync(
            ParcelOutboxEvents.DeliveredPendingConfirm,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await tripClient.Received(1).ReleaseCargoAsync(
            TripId,
            ParcelId,
            Arg.Any<decimal>(),
            Arg.Any<decimal>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unload_ConcurrentTransitionLoser_ReturnsInvalidStatusWithoutEventOrCargoRelease()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = AuthorizedTripClient();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryMarkUnloadedAsync(
                ParcelId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns((ParcelPaymentTransitionSnapshot?)null);
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(
                TripSnapshotOutcomeKind.Success,
                TripSnapshot(destinationArrivedAt: null),
                null));

        var handler = new UnloadParcelCommandHandler(
            repository,
            tripClient,
            outbox,
            Substitute.For<IUnitOfWork>());

        var action = () => handler.Handle(
            new UnloadParcelCommand(ParcelId, AssistantUserId, OperatorId),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("INVALID_STATUS");
        await outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await tripClient.DidNotReceive().ReleaseCargoAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<decimal>(),
            Arg.Any<decimal>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unload_CargoReleaseTripMissing_ReturnsTripServiceUnavailable()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = AuthorizedTripClient();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryMarkUnloadedAsync(
                ParcelId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.UNLOADED));
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(
                TripSnapshotOutcomeKind.Success,
                TripSnapshot(destinationArrivedAt: null),
                null));
        tripClient.ReleaseCargoAsync(
                TripId,
                ParcelId,
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.TripNotFound, "Trip missing."));

        var handler = new UnloadParcelCommandHandler(
            repository,
            tripClient,
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IUnitOfWork>());

        var action = () => handler.Handle(
            new UnloadParcelCommand(ParcelId, AssistantUserId, OperatorId),
            CancellationToken.None);

        var exception = (await action.Should()
            .ThrowAsync<ParcelDependencyUnavailableException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task Deliver_UnloadedParcel_SetsPendingConfirmAndEmitsOneEventWithoutCargoRelease()
    {
        var parcel = CreateParcel(ParcelStatus.UNLOADED);
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = AuthorizedTripClient();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryMarkDeliveredPendingConfirmAsync(
                ParcelId,
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Is<IReadOnlyCollection<string>?>(urls =>
                    urls != null && urls.SequenceEqual(new[] { DeliveryPhotoUrl })),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERED_PENDING_CONFIRM));

        var handler = new DeliverParcelCommandHandler(
            repository,
            tripClient,
            outbox,
            unitOfWork);

        var response = await handler.Handle(
            new DeliverParcelCommand(
                ParcelId,
                AssistantUserId,
                OperatorId,
                new[] { $"  {DeliveryPhotoUrl}  " }),
            CancellationToken.None);

        response.Status.Should().Be("DELIVERED_PENDING_CONFIRM");
        response.DeliveredPendingConfirmAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(5));
        await outbox.Received(1).EnqueueAsync(
            ParcelOutboxEvents.DeliveredPendingConfirm,
            Arg.Is<string>(payload => HasCanonicalDeliveryPayload(payload)),
            Arg.Any<CancellationToken>());
        await tripClient.DidNotReceive().ReleaseCargoAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<decimal>(),
            Arg.Any<decimal>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ParcelStatus.IN_TRANSIT)]
    [InlineData(ParcelStatus.DELIVERED_PENDING_CONFIRM)]
    public async Task Deliver_NonUnloadedStatus_ReturnsInvalidStatus(ParcelStatus status)
    {
        var parcel = CreateParcel(status);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        var handler = new DeliverParcelCommandHandler(
            repository,
            AuthorizedTripClient(),
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IUnitOfWork>());

        var action = () => handler.Handle(
            new DeliverParcelCommand(ParcelId, AssistantUserId, OperatorId, null),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("INVALID_STATUS");
        await repository.DidNotReceive().TryMarkDeliveredPendingConfirmAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<IReadOnlyCollection<string>?>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deliver_ConcurrentTransitionLoser_ReturnsInvalidStatusWithoutEvent()
    {
        var parcel = CreateParcel(ParcelStatus.UNLOADED);
        var repository = Substitute.For<IParcelRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryMarkDeliveredPendingConfirmAsync(
                ParcelId,
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns((ParcelPaymentTransitionSnapshot?)null);

        var handler = new DeliverParcelCommandHandler(
            repository,
            AuthorizedTripClient(),
            outbox,
            Substitute.For<IUnitOfWork>());

        var action = () => handler.Handle(
            new DeliverParcelCommand(ParcelId, AssistantUserId, OperatorId, null),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("INVALID_STATUS");
        await outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private static ITripServiceClient AuthorizedTripClient()
    {
        var client = Substitute.For<ITripServiceClient>();
        client.AuthorizeAssistantForTripAsync(
                TripId,
                AssistantUserId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        return client;
    }

    private static bool HasCanonicalDeliveryPayload(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var payload = document.RootElement;
        return payload.GetProperty("parcelId").GetGuid() == ParcelId
            && payload.GetProperty("parcelCode").GetString() == "VRP-001"
            && payload.GetProperty("operatorId").GetGuid() == OperatorId
            && payload.GetProperty("tripId").GetGuid() == TripId
            && payload.GetProperty("userId").GetGuid() == RecipientUserId
            && payload.GetProperty("deliveryToken").GetGuid() != Guid.Empty
            && payload.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow;
    }

    private static bool HasUnloadedPayload(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var payload = document.RootElement;
        var userIds = payload.GetProperty("userIds")
            .EnumerateArray()
            .Select(value => value.GetGuid())
            .ToHashSet();
        return payload.GetProperty("parcelId").GetGuid() == ParcelId
            && payload.GetProperty("tripId").GetGuid() == TripId
            && userIds.SetEquals(new[] { SenderUserId, RecipientUserId });
    }

    private static TripParcelSnapshot TripSnapshot(
        DateTimeOffset? destinationArrivedAt,
        string stopStatus = "ARRIVED")
        => new(
            TripId,
            OperatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "IN_PROGRESS",
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddHours(1),
            100_000,
            new TripStationDto(Guid.NewGuid(), "Origin"),
            new TripStationDto(Guid.NewGuid(), "Destination"),
            [new TripStopDto(
                DropoffStopId,
                1,
                false,
                true,
                DateTimeOffset.UtcNow,
                0,
                null,
                stopStatus,
                stopStatus == "ARRIVED" ? DateTimeOffset.UtcNow : null)],
            new TripSeatSummaryDto(40, 20),
            null,
            destinationArrivedAt);

    private static ParcelEntity CreateParcel(ParcelStatus status)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-001",
            SenderUserId,
            RecipientUserId,
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            "recipient@example.com",
            OperatorId,
            TripId,
            DropoffStopId,
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

    private static ParcelPaymentTransitionSnapshot Snapshot(ParcelStatus status)
        => new(
            ParcelId,
            "VRP-001",
            status,
            100_000,
            0,
            OperatorId,
            TripId,
            null,
            SenderUserId,
            ParcelSizeCategory.MEDIUM,
            null);

    private static void Set<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property!.SetValue(target, value);
    }
}
