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
            ValidRouteUnloadCommand(),
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
            ValidRouteUnloadCommand(),
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
        tripClient.GetTripOperationalLocationAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(OperationalLocation(currentStopId: null, currentStopStatus: null));

        var handler = new UnloadParcelCommandHandler(
            repository,
            tripClient,
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IUnitOfWork>());

        var action = () => handler.Handle(
            ValidRouteUnloadCommand(),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedValidationException>()).Which;
        exception.ErrorCode.Should().Be("DROP_OFF_STOP_NOT_ARRIVED");
        await repository.DidNotReceive().TryMarkUnloadedAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unload_StopPreviouslyArrivedButAlreadyDeparted_IsRejectedWithoutRelease()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = AuthorizedTripClient();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(
                TripSnapshotOutcomeKind.Success,
                TripSnapshot(
                    destinationArrivedAt: null,
                    actualDepartureTime: DateTimeOffset.UtcNow),
                null));
        tripClient.GetTripOperationalLocationAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(OperationalLocation(currentStopId: null, currentStopStatus: null));

        var handler = new UnloadParcelCommandHandler(
            repository,
            tripClient,
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IUnitOfWork>());

        var action = () => handler.Handle(
            new UnloadParcelCommand(
                ParcelId,
                AssistantUserId,
                OperatorId,
                ActualLocationKind: "ROUTE_STOP",
                ActualLocationId: DropoffStopId,
                ScannedParcelCode: "VRP-001"),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("PARCEL_CUSTODY_LOCATION_MISMATCH");
        await repository.DidNotReceive().TryMarkUnloadedAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await tripClient.DidNotReceive().ReleaseCargoAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<decimal>(),
            Arg.Any<decimal>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unload_ScannedQrBelongsToAnotherParcel_IsRejectedBeforeAuthorization()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        var handler = new UnloadParcelCommandHandler(
            repository,
            tripClient,
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IUnitOfWork>());

        var action = () => handler.Handle(
            new UnloadParcelCommand(
                ParcelId,
                AssistantUserId,
                OperatorId,
                ActualLocationKind: "ROUTE_STOP",
                ActualLocationId: DropoffStopId,
                ScannedParcelCode: "VRP-OTHER"),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("SCAN_IDENTITY_MISMATCH");
        await tripClient.DidNotReceive().AuthorizeAssistantForTripAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unload_WithoutQrScan_IsRejectedBeforeAuthorization()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        var handler = new UnloadParcelCommandHandler(
            repository,
            tripClient,
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IUnitOfWork>());

        var action = () => handler.Handle(
            new UnloadParcelCommand(
                ParcelId,
                AssistantUserId,
                OperatorId,
                ActualLocationKind: "ROUTE_STOP",
                ActualLocationId: DropoffStopId),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedValidationException>()).Which;
        exception.ErrorCode.Should().Be("PARCEL_SCAN_REQUIRED");
        await tripClient.DidNotReceive().AuthorizeAssistantForTripAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unload_WithoutActualLocation_IsRejectedBeforeTripLookup()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = AuthorizedTripClient();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        var handler = new UnloadParcelCommandHandler(
            repository,
            tripClient,
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IUnitOfWork>());

        var action = () => handler.Handle(
            new UnloadParcelCommand(
                ParcelId,
                AssistantUserId,
                OperatorId,
                ScannedParcelCode: "VRP-001"),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedValidationException>()).Which;
        exception.ErrorCode.Should().Be("PARCEL_CUSTODY_LOCATION_REQUIRED");
        await tripClient.DidNotReceive().GetTripParcelSnapshotAsync(
            Arg.Any<Guid>(),
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
            ValidRouteUnloadCommand(),
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
            ValidRouteUnloadCommand(),
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
            ValidRouteUnloadCommand(),
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
                Arg.Is<IReadOnlyCollection<string>?>(urls =>
                    urls != null && urls.SequenceEqual(new[] { DeliveryPhotoUrl })),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERED_PENDING_CONFIRM));

        var handler = new DeliverParcelCommandHandler(
            repository,
            Substitute.For<IParcelDeliveryTokenRepository>(),
            tripClient,
            Substitute.For<IParcelDeliveryEmailClient>(),
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
            Arg.Any<Guid>(),
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
            Substitute.For<IParcelDeliveryTokenRepository>(),
            AuthorizedTripClient(),
            Substitute.For<IParcelDeliveryEmailClient>(),
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IUnitOfWork>());

        var action = () => handler.Handle(
            new DeliverParcelCommand(ParcelId, AssistantUserId, OperatorId, null),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("INVALID_STATUS");
        await repository.DidNotReceive().TryMarkDeliveredPendingConfirmAsync(
            Arg.Any<Guid>(),
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
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns((ParcelPaymentTransitionSnapshot?)null);

        var handler = new DeliverParcelCommandHandler(
            repository,
            Substitute.For<IParcelDeliveryTokenRepository>(),
            AuthorizedTripClient(),
            Substitute.For<IParcelDeliveryEmailClient>(),
            outbox,
            Substitute.For<IUnitOfWork>());

        var action = () => handler.Handle(
            new DeliverParcelCommand(ParcelId, AssistantUserId, OperatorId, null),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("INVALID_STATUS");
        await outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<Guid>(),
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
        client.GetTripOperationalLocationAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(OperationalLocation());
        return client;
    }

    private static TripOperationalLocationOutcome OperationalLocation(
        Guid? currentStopId = null,
        string? currentStopStatus = "ARRIVED")
        => new(
            TripOperationalLocationOutcomeKind.Success,
            new TripOperationalLocationSnapshot(
                TripId,
                Guid.NewGuid(),
                "IN_PROGRESS",
                currentStopId ?? (currentStopStatus is null ? null : DropoffStopId),
                currentStopStatus,
                currentStopStatus is null ? null : DateTimeOffset.UtcNow,
                null,
                null),
            null);

    private static UnloadParcelCommand ValidRouteUnloadCommand()
        => new(
            ParcelId,
            AssistantUserId,
            OperatorId,
            ActualLocationKind: "ROUTE_STOP",
            ActualLocationId: DropoffStopId,
            ScannedParcelCode: "VRP-001");

    private static bool HasCanonicalDeliveryPayload(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var payload = document.RootElement;
        return payload.GetProperty("parcelId").GetGuid() == ParcelId
            && payload.GetProperty("eventId").GetGuid() != Guid.Empty
            && payload.GetProperty("occurredAt").GetDateTimeOffset() <= DateTimeOffset.UtcNow
            && payload.GetProperty("parcelCode").GetString() == "VRP-001"
            && payload.GetProperty("operatorId").GetGuid() == OperatorId
            && payload.GetProperty("tripId").GetGuid() == TripId
            && payload.GetProperty("userId").GetGuid() == RecipientUserId
            && payload.GetProperty("recipientUserIds")[0].GetGuid() == RecipientUserId
            && payload.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow
            && !payload.TryGetProperty("deliveryToken", out _)
            && !payload.TryGetProperty("recipientEmail", out _);
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
        string stopStatus = "ARRIVED",
        DateTimeOffset? actualDepartureTime = null)
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
                stopStatus == "ARRIVED" ? DateTimeOffset.UtcNow : null,
                actualDepartureTime)],
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
