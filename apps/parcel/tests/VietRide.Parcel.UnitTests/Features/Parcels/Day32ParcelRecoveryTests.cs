using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.ManualCancel;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Parcels;

public sealed class Day32ParcelRecoveryTests
{
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid SenderUserId = Guid.NewGuid();

    [Fact]
    public async Task ManualCancel_PolicyUsesOutstandingAndAwayFromZeroWithoutFloor()
    {
        var requestId = Guid.NewGuid();
        var parcel = CreateParcel(
            ParcelStatus.CHECKED_IN,
            depositPaidVnd: 100_001);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>())
            .Returns(parcel);
        repository.TryManualCancelAsync(
                ParcelId,
                OperatorId,
                ParcelStatus.CANCELLED,
                "sender request",
                50_001,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.CANCELLED));
        var identity = Substitute.For<IIdentityServiceClient>();
        identity.GetOperatorInfoAsync(OperatorId, Arg.Any<CancellationToken>())
            .Returns(new OperatorLookupOutcome(
                OperatorLookupOutcomeKind.Success,
                new IdentityOperatorInfo(
                    OperatorId,
                    "Operator",
                    new ParcelNoShowPolicy(50m, 30)),
                null));
        var tripClient = SuccessfulIdempotentTripClient();
        var outbox = CapturingOutbox(out var events);

        var result = await new ManualCancelParcelCommandHandler(
                repository,
                identity,
                tripClient,
                outbox,
                Substitute.For<IParcelStatsRepository>())
            .Handle(
                new ManualCancelParcelCommand(
                    ParcelId,
                    OperatorId,
                    " sender request ",
                    "POLICY",
                    requestId),
                CancellationToken.None);

        result.Status.Should().Be("CANCELLED");
        result.RefundChoice.Should().Be("POLICY");
        result.RefundAmount.Should().Be(50_001);
        var refundPayload = events.Single(item =>
            item.Type == ParcelOutboxEvents.RefundInitiated).Payload;
        using var json = JsonDocument.Parse(refundPayload);
        json.RootElement.GetProperty("amount").GetInt64().Should().Be(50_001);
        json.RootElement.GetProperty("reason").GetString()
            .Should().Be("MANUAL_CANCEL_POLICY");
        await ((IIdempotentTripServiceClient)tripClient).Received(1)
            .ReleaseCargoAsync(
                TripId,
                ParcelId,
                5m,
                0.0001m,
                Arg.Is<Guid>(key => key.ToString("D")[14] == '4'),
                Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ParcelStatus.PENDING_OPERATOR_REVIEW)]
    [InlineData(ParcelStatus.PENDING_PAYMENT)]
    public async Task ManualCancel_EarlyPreLoadStatuses_AlwaysBecomeCancelled(
        ParcelStatus status)
    {
        var parcel = CreateParcel(status);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>())
            .Returns(parcel);
        repository.TryManualCancelAsync(
                ParcelId,
                OperatorId,
                ParcelStatus.CANCELLED,
                "sender request",
                0,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.CANCELLED));

        var result = await new ManualCancelParcelCommandHandler(
                repository,
                Substitute.For<IIdentityServiceClient>(),
                SuccessfulIdempotentTripClient(),
                Substitute.For<IIntegrationEventOutbox>(),
                Substitute.For<IParcelStatsRepository>())
            .Handle(
                new ManualCancelParcelCommand(
                    ParcelId,
                    OperatorId,
                    "sender request",
                    "NO",
                    Guid.NewGuid()),
                CancellationToken.None);

        result.Status.Should().Be("CANCELLED");
        await repository.Received(1).TryManualCancelAsync(
            ParcelId,
            OperatorId,
            ParcelStatus.CANCELLED,
            "sender request",
            0,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManualCancel_MalformedPolicyFailsClosedBeforeStateOrCargo()
    {
        var parcel = CreateParcel(
            ParcelStatus.RESERVED,
            depositPaidVnd: 100_000);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>())
            .Returns(parcel);
        var identity = Substitute.For<IIdentityServiceClient>();
        identity.GetOperatorInfoAsync(OperatorId, Arg.Any<CancellationToken>())
            .Returns(new OperatorLookupOutcome(
                OperatorLookupOutcomeKind.Success,
                new IdentityOperatorInfo(
                    OperatorId,
                    "Operator",
                    new ParcelNoShowPolicy(101m, 30)),
                null));
        var tripClient = SuccessfulIdempotentTripClient();

        var act = () => new ManualCancelParcelCommandHandler(
                repository,
                identity,
                tripClient,
                Substitute.For<IIntegrationEventOutbox>(),
                Substitute.For<IParcelStatsRepository>())
            .Handle(
                new ManualCancelParcelCommand(
                    ParcelId,
                    OperatorId,
                    "sender request",
                    "POLICY",
                    Guid.NewGuid()),
                CancellationToken.None);

        await act.Should().ThrowAsync<ParcelDependencyUnavailableException>()
            .Where(exception =>
                exception.StatusCode == 503
                && exception.ErrorCode == "UPSTREAM_UNAVAILABLE");
        await repository.DidNotReceiveWithAnyArgs().TryManualCancelAsync(
            default,
            default,
            default,
            default!,
            default,
            default,
            default);
        await ((IIdempotentTripServiceClient)tripClient)
            .DidNotReceiveWithAnyArgs()
            .ReleaseCargoAsync(
                default,
                default,
                default,
                default,
                default);
    }

    [Fact]
    public async Task PendingOperatorActionTransfer_ClaimsBeforeDelegatingDurableOperation()
    {
        var requestId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var targetTripId = Guid.NewGuid();
        var parcel = CreateParcel(ParcelStatus.PENDING_OPERATOR_ACTION);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>())
            .Returns(parcel);
        var sequence = new List<string>();
        repository.GetActiveCargoRecoveryOperationAsync(
                ParcelId,
                Arg.Any<CancellationToken>())
            .Returns((ParcelCargoRecoveryOperationSnapshot?)null);
        repository.TryClaimCargoRecoveryTransferAsync(
                requestId,
                ParcelId,
                OperatorId,
                targetTripId,
                actorId,
                "trip cancelled",
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sequence.Add("parcel-claim");
                return CargoOperation(
                    requestId,
                    ParcelCargoRecoveryOperationType.TRANSFER,
                    targetTripId: targetTripId);
            });
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.GetTripParcelSnapshotAsync(
                targetTripId,
                Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(
                TripSnapshotOutcomeKind.Success,
                TripSnapshot(targetTripId),
                null));
        var mediator = Substitute.For<IMediator>();
        mediator.Send(
                Arg.Is<ResumeCargoRecoveryOperationCommand>(
                    command => command.OperationId == requestId),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sequence.Add("resume-operation");
                return new OperationalParcelResponse(
                    ParcelId,
                    "VRP-001",
                    "RESERVED",
                    TripId: targetTripId);
            });
        var unitOfWork = UnitOfWork();

        var result = await new RequestTransferCommandHandler(
                repository,
                tripClient,
                Substitute.For<IIntegrationEventOutbox>(),
                unitOfWork,
                mediator,
                Clock())
            .Handle(
                new RequestTransferCommand(
                    ParcelId,
                    OperatorId,
                    targetTripId,
                    "trip cancelled",
                    requestId,
                    actorId),
                CancellationToken.None);

        sequence.Should().Equal("parcel-claim", "resume-operation");
        result.Status.Should().Be("RESERVED");
        result.TripId.Should().Be(targetTripId);
        result.TransferTargetTripId.Should().BeNull();
        await tripClient.DidNotReceiveWithAnyArgs()
            .TransferCargoAsync(
                default,
                default,
                default,
                default!,
                default,
                default,
                default);
    }

    [Fact]
    public async Task DurableTransfer_CapacityFailureMarksOperationFailedWithoutParcelCompletion()
    {
        var requestId = Guid.NewGuid();
        var targetTripId = Guid.NewGuid();
        var repository = Substitute.For<IParcelRepository>();
        repository.GetCargoRecoveryOperationAsync(
                requestId,
                Arg.Any<CancellationToken>())
            .Returns(CargoOperation(
                requestId,
                ParcelCargoRecoveryOperationType.TRANSFER,
                targetTripId: targetTripId));
        repository.TryFailCargoRecoveryOperationAsync(
                requestId,
                "TRIP_CARGO_CAPACITY_EXCEEDED",
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.TransferCargoAsync(
                TripId,
                ParcelId,
                targetTripId,
                "RESERVED",
                false,
                requestId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCargoTransferOutcome(
                TripCargoTransferOutcomeKind.CapacityExceeded,
                "full"));

        var act = () => new ResumeCargoRecoveryOperationCommandHandler(
                repository,
                tripClient,
                Substitute.For<IIntegrationEventOutbox>(),
                Substitute.For<IParcelStatsRepository>(),
                UnitOfWork(),
                Clock())
            .Handle(
                new ResumeCargoRecoveryOperationCommand(requestId),
                CancellationToken.None);

        await act.Should().ThrowAsync<CodedValidationException>()
            .Where(exception =>
                exception.ErrorCode == "TRIP_CARGO_CAPACITY_EXCEEDED");
        await repository.DidNotReceiveWithAnyArgs()
            .TryCompleteCargoRecoveryTransferAsync(
                default,
                default,
                default);
    }

    [Fact]
    public async Task Return_ReleasesCargoAndRefundsRemainingCollectedAmount()
    {
        var actorId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var parcel = CreateParcel(
            ParcelStatus.PENDING_OPERATOR_ACTION,
            depositPaidVnd: 150_000,
            balancePaidVnd: 50_000,
            refundedAmountVnd: 25_000);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetCargoRecoveryOperationAsync(
                requestId,
                Arg.Any<CancellationToken>())
            .Returns(CargoOperation(
                requestId,
                ParcelCargoRecoveryOperationType.RETURN,
                actorId,
                refundAmountVnd: 175_000,
                refundDueVnd: 200_000));
        repository.TryCompleteCargoRecoveryReturnAsync(
                requestId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.RETURNED));
        var tripClient = SuccessfulIdempotentTripClient();
        var outbox = CapturingOutbox(out var events);

        var result = await new ResumeCargoRecoveryOperationCommandHandler(
                repository,
                tripClient,
                outbox,
                Substitute.For<IParcelStatsRepository>(),
                UnitOfWork(),
                Clock())
            .Handle(
                new ResumeCargoRecoveryOperationCommand(requestId),
                CancellationToken.None);

        result.Status.Should().Be("RETURNED");
        result.RefundAmount.Should().Be(175_000);
        var refundPayload = events.Single(item =>
            item.Type == ParcelOutboxEvents.RefundInitiated).Payload;
        using var json = JsonDocument.Parse(refundPayload);
        json.RootElement.GetProperty("amount").GetInt64().Should().Be(175_000);
        json.RootElement.GetProperty("reason").GetString()
            .Should().Be("OPERATOR_RETURN");
        await ((IIdempotentTripServiceClient)tripClient).Received(1)
            .ReleaseCargoAsync(
                TripId,
                ParcelId,
                5m,
                0.0001m,
                requestId,
                Arg.Any<CancellationToken>());
    }

    private static ParcelEntity CreateParcel(
        ParcelStatus status,
        long depositPaidVnd = 0,
        long balancePaidVnd = 0,
        long refundedAmountVnd = 0)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-001",
            SenderUserId,
            null,
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            "recipient@example.com",
            OperatorId,
            TripId,
            null,
            null,
            "Item",
            null,
            ParcelSizeCategory.MEDIUM,
            5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(200_000));
        Set(parcel, nameof(parcel.Id), ParcelId);
        Set(parcel, nameof(parcel.Status), status);
        Set(parcel, nameof(parcel.DepositPaidVnd), Money.FromRaw(depositPaidVnd));
        Set(parcel, nameof(parcel.BalancePaidVnd), Money.FromRaw(balancePaidVnd));
        Set(parcel, nameof(parcel.RefundedAmountVnd), Money.FromRaw(refundedAmountVnd));
        return parcel;
    }

    private static ParcelPaymentTransitionSnapshot Snapshot(
        ParcelStatus status,
        Guid? tripId = null)
        => new(
            ParcelId,
            "VRP-001",
            status,
            0,
            0,
            OperatorId,
            tripId ?? TripId,
            null,
            SenderUserId,
            ParcelSizeCategory.MEDIUM,
            null);

    private static ParcelCargoRecoveryOperationSnapshot CargoOperation(
        Guid operationId,
        ParcelCargoRecoveryOperationType operationType,
        Guid? actorId = null,
        Guid? targetTripId = null,
        long refundAmountVnd = 0,
        long refundDueVnd = 0)
        => new(
            operationId,
            ParcelId,
            "VRP-001",
            OperatorId,
            SenderUserId,
            operationType,
            ParcelCargoRecoveryOperationStatus.PENDING,
            TripId,
            targetTripId,
            operationType == ParcelCargoRecoveryOperationType.TRANSFER
                ? "RESERVED"
                : null,
            actorId ?? Guid.NewGuid(),
            operationType == ParcelCargoRecoveryOperationType.TRANSFER
                ? "trip cancelled"
                : "return to sender",
            refundAmountVnd,
            refundDueVnd,
            ParcelStatus.PENDING_OPERATOR_ACTION,
            false,
            DateTimeOffset.UtcNow,
            null,
            null,
            5m,
            0.0001m,
            ParcelStatus.PENDING_OPERATOR_ACTION,
            TripId,
            null);

    private static TripParcelSnapshot TripSnapshot(Guid tripId)
        => new(
            tripId,
            OperatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SCHEDULED",
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(5),
            100_000,
            new TripStationDto(Guid.NewGuid(), "Origin"),
            new TripStationDto(Guid.NewGuid(), "Destination"),
            [],
            new TripSeatSummaryDto(40, 40),
            null,
            null);

    private static ITripServiceClient SuccessfulIdempotentTripClient()
    {
        var client = Substitute.For<ITripServiceClient, IIdempotentTripServiceClient>();
        ((IIdempotentTripServiceClient)client).ReleaseCargoAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
        return client;
    }

    private static IIntegrationEventOutbox CapturingOutbox(
        out List<(Guid Id, string Type, string Payload)> events)
    {
        var captured = new List<(Guid Id, string Type, string Payload)>();
        events = captured;
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        outbox.EnqueueAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => captured.Add((
                call.ArgAt<Guid>(0),
                call.ArgAt<string>(1),
                call.ArgAt<string>(2))));
        outbox.EnqueueAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return outbox;
    }

    private static IUnitOfWork UnitOfWork()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(1);
        unitOfWork.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        unitOfWork.RollbackAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return unitOfWork;
    }

    private static IClock Clock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        return clock;
    }

    private static void Set<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property.Should().NotBeNull();
        property!.SetValue(target, value);
    }
}
