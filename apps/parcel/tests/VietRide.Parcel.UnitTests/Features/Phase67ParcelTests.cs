using System.Reflection;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.AccessCheck;
using VietRide.Parcel.Application.Features.Parcels.ConfirmDelivery;
using VietRide.Parcel.Application.Features.Parcels.ManualConfirmDelivery;
using VietRide.Parcel.Application.Features.Parcels.MarkLoaded;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;
using VietRide.Parcel.Application.Features.Parcels.Received;
using VietRide.Parcel.Application.Features.Parcels.RejectDelivery;
using VietRide.Parcel.Application.Features.Parcels.TripEvents;
using VietRide.Parcel.Application.Features.Parcels.UndoRejectDelivery;
using VietRide.Parcel.Application.Features.Parcels.Unload;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class Phase67ParcelTests
{
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid SenderUserId = Guid.NewGuid();
    private static readonly Guid RecipientUserId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid DropoffStopId = Guid.NewGuid();

    [Fact]
    public async Task MarkLoaded_HappyPath_UsesAtomicRepositoryTransition()
    {
        var parcel = CreateParcel(ParcelStatus.PENDING);
        var repo = Substitute.For<IParcelRepository>();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repo.TryMarkLoadedAsync(ParcelId, TripId, "VRP-001", null, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.LOADED));

        var handler = new MarkParcelLoadedCommandHandler(repo, TripCargo(), Outbox(), Stats());
        var result = await handler.Handle(new MarkParcelLoadedCommand(ParcelId, TripId, "VRP-001", null), default);

        result.Status.Should().Be("LOADED");
        await repo.Received(1).TryMarkLoadedAsync(
            ParcelId, TripId, "VRP-001", null, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkLoaded_WrongTripOrCode_ReturnsParcelNotFound()
    {
        var parcel = CreateParcel(ParcelStatus.PENDING);
        var repo = Substitute.For<IParcelRepository>();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);

        var handler = new MarkParcelLoadedCommandHandler(repo, TripCargo(), Outbox(), Stats());
        var act = () => handler.Handle(new MarkParcelLoadedCommand(ParcelId, Guid.NewGuid(), "WRONG", null), default);

        await act.Should().ThrowAsync<CodedNotFoundException>()
            .Where(e => e.ErrorCode == "PARCEL_NOT_FOUND");
    }

    [Fact]
    public async Task Unload_NonOwner_ReturnsForbiddenBeforeStatusLeak()
    {
        var parcel = CreateParcel(ParcelStatus.PENDING);
        var repo = Substitute.For<IParcelRepository>();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);

        var handler = new UnloadParcelCommandHandler(repo, Substitute.For<ITripServiceClient>(), Outbox(), UnitOfWork());
        var act = () => handler.Handle(new UnloadParcelCommand(ParcelId, Guid.NewGuid()), default);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.ErrorCode == "FORBIDDEN");
    }

    [Fact]
    public async Task Unload_HappyPath_ValidatesDropoffAndSetsPendingConfirm()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        var repo = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success, TripSnapshot(allowDropoff: true), null));
        tripClient.ReleaseCargoAsync(TripId, ParcelId, Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
        repo.TryUnloadToPendingConfirmAsync(ParcelId, Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERED_PENDING_CONFIRM));

        var handler = new UnloadParcelCommandHandler(repo, tripClient, Outbox(), UnitOfWork());
        var result = await handler.Handle(new UnloadParcelCommand(ParcelId, OperatorId), default);

        result.Status.Should().Be("DELIVERED_PENDING_CONFIRM");
    }

    [Fact]
    public async Task Unload_WhenDropoffStopMissing_RequiresFinalStopArrived()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        Set<Guid?>(parcel, nameof(parcel.DropoffStopId), null);
        var repo = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(
                TripSnapshotOutcomeKind.Success,
                TripSnapshot(
                    allowDropoff: true,
                    stops:
                    [
                        new TripStopDto(Guid.NewGuid(), 1, true, false, DateTimeOffset.UtcNow, 0, null, "ARRIVED", DateTimeOffset.UtcNow),
                        new TripStopDto(Guid.NewGuid(), 2, false, true, DateTimeOffset.UtcNow, 20, null, "PENDING", null),
                    ]),
                null));

        var handler = new UnloadParcelCommandHandler(repo, tripClient, Outbox(), UnitOfWork());
        var act = () => handler.Handle(new UnloadParcelCommand(ParcelId, OperatorId), default);

        await act.Should().ThrowAsync<CodedValidationException>()
            .Where(e => e.ErrorCode == "DROP_OFF_STOP_NOT_ARRIVED");
    }

    [Fact]
    public async Task Unload_WhenDropoffStopMissing_AllowsAfterFinalStopArrived()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        Set<Guid?>(parcel, nameof(parcel.DropoffStopId), null);
        var repo = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(
                TripSnapshotOutcomeKind.Success,
                TripSnapshot(
                    allowDropoff: true,
                    stops:
                    [
                        new TripStopDto(Guid.NewGuid(), 1, true, false, DateTimeOffset.UtcNow, 0, null, "ARRIVED", DateTimeOffset.UtcNow),
                        new TripStopDto(Guid.NewGuid(), 2, false, true, DateTimeOffset.UtcNow, 20, null, "ARRIVED", DateTimeOffset.UtcNow),
                    ]),
                null));
        tripClient.ReleaseCargoAsync(TripId, ParcelId, Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
        repo.TryUnloadToPendingConfirmAsync(ParcelId, Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERED_PENDING_CONFIRM));

        var handler = new UnloadParcelCommandHandler(repo, tripClient, Outbox(), UnitOfWork());
        var result = await handler.Handle(new UnloadParcelCommand(ParcelId, OperatorId), default);

        result.Status.Should().Be("DELIVERED_PENDING_CONFIRM");
    }

    [Fact]
    public async Task Received_TripNotFound_IsBestEffortAndKeepsPage()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        var repo = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        repo.ListReceivedByUserIdAsync(RecipientUserId, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.TripNotFound, null, null));

        var handler = new GetReceivedParcelsQueryHandler(repo, tripClient);
        var result = await handler.Handle(new GetReceivedParcelsQuery(RecipientUserId, 1, 20), default);

        result.Items.Should().HaveCount(1);
        result.Items[0].OriginStation.Should().BeNull();
        result.Items[0].DestinationStation.Should().BeNull();
    }

    [Fact]
    public async Task Received_Success_UsesNestedStationContractShape()
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        var repo = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        repo.ListReceivedByUserIdAsync(RecipientUserId, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success, TripSnapshot(allowDropoff: true), null));

        var handler = new GetReceivedParcelsQueryHandler(repo, tripClient);
        var result = await handler.Handle(new GetReceivedParcelsQuery(RecipientUserId, 1, 20), default);

        var originStation = result.Items[0].OriginStation;
        var destinationStation = result.Items[0].DestinationStation;
        originStation.Should().NotBeNull();
        destinationStation.Should().NotBeNull();
        originStation!.Id.Should().NotBeEmpty();
        originStation.Name.Should().Be("Origin");
        destinationStation!.Name.Should().Be("Destination");
    }

    [Theory]
    [InlineData("SENDER")]
    [InlineData("RECIPIENT")]
    [InlineData("OPERATOR")]
    [InlineData("NONE")]
    public async Task AccessCheck_CoversAllRoles(string expectedRole)
    {
        var parcel = CreateParcel(ParcelStatus.IN_TRANSIT);
        var repo = Substitute.For<IParcelRepository>();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);

        var userId = expectedRole switch
        {
            "SENDER" => SenderUserId,
            "RECIPIENT" => RecipientUserId,
            _ => Guid.NewGuid(),
        };
        var operatorId = expectedRole == "OPERATOR" ? OperatorId : (Guid?)null;

        var handler = new GetParcelAccessCheckQueryHandler(repo);
        var result = await handler.Handle(new GetParcelAccessCheckQuery(ParcelId, userId, operatorId), default);

        result.Role.Should().Be(expectedRole);
        result.Allowed.Should().Be(expectedRole != "NONE");
    }

    [Fact]
    public async Task ConfirmDelivery_InvalidToken_Returns400Code()
    {
        var repo = Substitute.For<IParcelRepository>();
        repo.FindByDeliveryTokenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ParcelEntity?)null);

        var handler = new ConfirmDeliveryCommandHandler(repo, Outbox(), Stats());
        var act = () => handler.Handle(new ConfirmDeliveryCommand(Guid.NewGuid(), "127.0.0.1"), default);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "PARCEL_DELIVERY_TOKEN_INVALID");
    }

    [Fact]
    public async Task ConfirmDelivery_WrongStatus_ReturnsParcelNotPendingConfirm400Code()
    {
        var token = Guid.NewGuid();
        var parcel = CreateParcel(ParcelStatus.PENDING, token, DateTimeOffset.UtcNow.AddHours(1));
        var repo = Substitute.For<IParcelRepository>();
        repo.FindByDeliveryTokenAsync(token, Arg.Any<CancellationToken>()).Returns(parcel);

        var handler = new ConfirmDeliveryCommandHandler(repo, Outbox(), Stats());
        var act = () => handler.Handle(new ConfirmDeliveryCommand(token, "127.0.0.1"), default);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "PARCEL_NOT_PENDING_CONFIRM");
    }

    [Fact]
    public async Task ConfirmDelivery_HappyPath_ConfirmsDelivery()
    {
        var token = Guid.NewGuid();
        var parcel = CreateParcel(ParcelStatus.DELIVERED_PENDING_CONFIRM, token, DateTimeOffset.UtcNow.AddHours(1));
        var repo = Substitute.For<IParcelRepository>();
        repo.FindByDeliveryTokenAsync(token, Arg.Any<CancellationToken>()).Returns(parcel);
        repo.TryConfirmDeliveryAsync(ParcelId, token, "127.0.0.1", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERY_CONFIRMED));

        var handler = new ConfirmDeliveryCommandHandler(repo, Outbox(), Stats());
        var result = await handler.Handle(new ConfirmDeliveryCommand(token, "127.0.0.1"), default);

        result.Status.Should().Be("DELIVERY_CONFIRMED");
        result.ConfirmedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RejectDelivery_WrongStatus_ReturnsParcelNotPendingConfirm400Code()
    {
        var token = Guid.NewGuid();
        var parcel = CreateParcel(ParcelStatus.DELIVERY_CONFIRMED, token, DateTimeOffset.UtcNow.AddHours(1));
        var repo = Substitute.For<IParcelRepository>();
        repo.FindByDeliveryTokenAsync(token, Arg.Any<CancellationToken>()).Returns(parcel);

        var handler = new RejectDeliveryCommandHandler(repo, Outbox(), Stats());
        var act = () => handler.Handle(new RejectDeliveryCommand(token, "damaged"), default);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "PARCEL_NOT_PENDING_CONFIRM");
    }

    [Fact]
    public async Task RejectDelivery_HappyPath_ReturnsCanUndoUntil()
    {
        var token = Guid.NewGuid();
        var parcel = CreateParcel(ParcelStatus.DELIVERED_PENDING_CONFIRM, token, DateTimeOffset.UtcNow.AddHours(1));
        var repo = Substitute.For<IParcelRepository>();
        repo.FindByDeliveryTokenAsync(token, Arg.Any<CancellationToken>()).Returns(parcel);
        repo.TryRejectDeliveryAsync(ParcelId, token, "damaged", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERY_REJECTED));

        var handler = new RejectDeliveryCommandHandler(repo, Outbox(), Stats());
        var result = await handler.Handle(new RejectDeliveryCommand(token, "damaged"), default);

        result.Status.Should().Be("DELIVERY_REJECTED");
        result.CanUndoUntil.Should().Be(result.RejectedAt.AddMinutes(15));
    }

    [Fact]
    public async Task ConfirmDelivery_RejectedStatus_ReturnsParcelNotPendingConfirm400Code()
    {
        var token = Guid.NewGuid();
        var parcel = CreateParcel(ParcelStatus.DELIVERY_REJECTED, token, DateTimeOffset.UtcNow.AddHours(1));
        var repo = Substitute.For<IParcelRepository>();
        repo.FindByDeliveryTokenAsync(token, Arg.Any<CancellationToken>()).Returns(parcel);

        var handler = new ConfirmDeliveryCommandHandler(repo, Outbox(), Stats());
        var act = () => handler.Handle(new ConfirmDeliveryCommand(token, "127.0.0.1"), default);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "PARCEL_NOT_PENDING_CONFIRM");
    }

    [Fact]
    public async Task UndoRejectDelivery_HappyPath_DecrementsRejectedStat()
    {
        var token = Guid.NewGuid();
        var parcel = CreateParcel(ParcelStatus.DELIVERY_REJECTED, token, DateTimeOffset.UtcNow.AddHours(1));
        Set<DateTimeOffset?>(parcel, nameof(parcel.RejectedAt), DateTimeOffset.UtcNow.AddMinutes(-5));
        var repo = Substitute.For<IParcelRepository>();
        var stats = Stats();
        repo.FindByDeliveryTokenAsync(token, Arg.Any<CancellationToken>()).Returns(parcel);
        repo.TryUndoRejectDeliveryAsync(ParcelId, token, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERED_PENDING_CONFIRM));

        var handler = new UndoRejectDeliveryCommandHandler(repo, Outbox(), stats);
        var result = await handler.Handle(new UndoRejectDeliveryCommand(token), default);

        result.Status.Should().Be("DELIVERED_PENDING_CONFIRM");
        await stats.Received(1).UpsertIncrementAsync(
            OperatorId,
            Arg.Any<DateOnly>(),
            0, 0, 0, -1, 0, 0, 0,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManualConfirmDelivery_WrongOperator_ReturnsForbidden()
    {
        var parcel = CreateParcel(ParcelStatus.DELIVERED_PENDING_CONFIRM);
        var repo = Substitute.For<IParcelRepository>();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);

        var handler = new ManualConfirmDeliveryCommandHandler(repo, Outbox(), Stats());
        var act = () => handler.Handle(new ManualConfirmDeliveryCommand(ParcelId, Guid.NewGuid(), Guid.NewGuid(), "verified"), default);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.ErrorCode == "FORBIDDEN");
    }

    [Fact]
    public async Task ManualConfirmDelivery_HappyPath_IncrementsDeliveredStat()
    {
        var actorUserId = Guid.NewGuid();
        var parcel = CreateParcel(ParcelStatus.DELIVERED_PENDING_CONFIRM);
        var repo = Substitute.For<IParcelRepository>();
        var stats = Stats();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repo.TryManualConfirmDeliveryAsync(ParcelId, OperatorId, actorUserId, "verified", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERY_CONFIRMED));

        var handler = new ManualConfirmDeliveryCommandHandler(repo, Outbox(), stats);
        var result = await handler.Handle(new ManualConfirmDeliveryCommand(ParcelId, actorUserId, OperatorId, "verified"), default);

        result.Status.Should().Be("DELIVERY_CONFIRMED");
        await stats.Received(1).UpsertIncrementAsync(
            OperatorId,
            Arg.Any<DateOnly>(),
            0, 0, 1, 0, 0, 0, 0,
            Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task StatusOverride_ReturnsParcelAndEmitsAuditEvent()
    {
        var actorUserId = Guid.NewGuid();
        var parcel = CreateParcel(ParcelStatus.PENDING_OPERATOR_ACTION);
        var repo = Substitute.For<IParcelRepository>();
        var identity = Substitute.For<IIdentityServiceClient>();
        var outbox = Outbox();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repo.TryReturnAsync(ParcelId, OperatorId, actorUserId, "customer no-show", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.RETURNED));
        identity.GetOperatorInfoAsync(OperatorId, Arg.Any<CancellationToken>())
            .Returns(new OperatorLookupOutcome(
                OperatorLookupOutcomeKind.Success,
                new IdentityOperatorInfo(OperatorId, "Operator", ParcelNoShowPolicy.Default),
                null));

        var handler = new ReturnParcelCommandHandler(repo, identity, TripCargo(), outbox, Stats());
        var result = await handler.Handle(
            new ReturnParcelCommand(ParcelId, OperatorId, actorUserId, "customer no-show", IsStatusOverride: true),
            default);

        result.Status.Should().Be("RETURNED");
        await outbox.Received(1).EnqueueAsync(
            ParcelOutboxEvents.StatusOverridden,
            Arg.Is<string>(payload => payload.Contains("customer no-show", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task TripStartedCommand_IsIdempotentBulkLoadedToInTransit()
    {
        var repo = Substitute.For<IParcelRepository>();
        repo.TryBulkSetInTransitByTripIdAsync(TripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([
                EventSnapshot(ParcelStatus.IN_TRANSIT),
                EventSnapshot(ParcelStatus.IN_TRANSIT),
            ]);

        var result = await new HandleTripStartedCommandHandler(repo)
            .Handle(new HandleTripStartedCommand(TripId), default);

        result.Should().Be(2);
    }

    [Fact]
    public async Task TripCompletedCommand_IsIdempotentBulkUnresolvedToPendingOperatorAction()
    {
        var repo = Substitute.For<IParcelRepository>();
        repo.TryBulkSetPendingOperatorActionByTripIdAsync(TripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([
                EventSnapshot(ParcelStatus.PENDING_OPERATOR_ACTION),
                EventSnapshot(ParcelStatus.PENDING_OPERATOR_ACTION),
                EventSnapshot(ParcelStatus.PENDING_OPERATOR_ACTION),
            ]);

        var result = await new HandleTripCompletedCommandHandler(repo)
            .Handle(new HandleTripCompletedCommand(TripId), default);

        result.Should().Be(3);
    }

    private static ParcelEntity CreateParcel(
        ParcelStatus status,
        Guid? deliveryToken = null,
        DateTimeOffset? deliveryTokenExpiresAt = null)
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
        Set(parcel, nameof(parcel.DeliveryToken), deliveryToken);
        Set(parcel, nameof(parcel.DeliveryTokenExpiresAt), deliveryTokenExpiresAt);
        Set<DateTimeOffset?>(parcel, nameof(parcel.DeliveryTokenRevokedAt), null);
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

    private static TripParcelSnapshot TripSnapshot(bool allowDropoff, IReadOnlyList<TripStopDto>? stops = null)
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
            stops ?? [new TripStopDto(DropoffStopId, 1, false, allowDropoff, DateTimeOffset.UtcNow, 10, null, "ARRIVED", DateTimeOffset.UtcNow)],
            new TripSeatSummaryDto(40, 10),
            null);

    private static void Set<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property!.SetValue(target, value);
    }

    private static ParcelEventSnapshot EventSnapshot(ParcelStatus status)
        => new(Guid.NewGuid(), "VRP-001", OperatorId, TripId, status);

    private static IIntegrationEventOutbox Outbox()
        => Substitute.For<IIntegrationEventOutbox>();

    private static ITripServiceClient TripCargo()
    {
        var trip = Substitute.For<ITripServiceClient>();
        trip.LoadCargoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
        trip.ReleaseCargoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
        return trip;
    }

    private static IUnitOfWork UnitOfWork()
        => Substitute.For<IUnitOfWork>();

    private static IParcelStatsRepository Stats()
        => Substitute.For<IParcelStatsRepository>();
}
