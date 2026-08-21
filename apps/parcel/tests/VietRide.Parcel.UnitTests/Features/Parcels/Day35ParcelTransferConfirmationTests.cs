using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using VietRide.Parcel.Api.Controllers;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.UnitTests.Features.Parcels;

public sealed class Day35ParcelTransferConfirmationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 4, 30, 0, TimeSpan.Zero);
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid SourceTripId = Guid.NewGuid();
    private static readonly Guid TargetTripId = Guid.NewGuid();
    private static readonly Guid CrewUserId = Guid.NewGuid();
    private static readonly Guid SenderUserId = Guid.NewGuid();
    private const string ParcelCode = "VRP-DAY35-TRANSFER";

    [Fact]
    public async Task CrewConfirmation_ClaimsThenTransfersThenFinalizesWithOneOutboxEvent()
    {
        var requestKey = Guid.NewGuid();
        var pending = Snapshot();
        var claimed = Snapshot(
            claimId: requestKey,
            claimedAt: Now,
            claimedByUserId: CrewUserId);
        var completed = Snapshot(
            status: ParcelStatus.LOADED,
            sourceTripId: TargetTripId,
            claimId: requestKey,
            claimedAt: Now,
            claimedByUserId: CrewUserId,
            confirmedAt: Now,
            confirmedByUserId: CrewUserId);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetTransferConfirmationSnapshotAsync(
                ParcelId,
                Arg.Any<CancellationToken>())
            .Returns(pending);
        repository.TryClaimTransferConfirmationAsync(
                ParcelId,
                ParcelCode,
                SourceTripId,
                TargetTripId,
                requestKey,
                CrewUserId,
                Now,
                Arg.Any<CancellationToken>())
            .Returns(claimed);
        repository.TryCompleteTransferConfirmationAsync(
                ParcelId,
                SourceTripId,
                TargetTripId,
                requestKey,
                CrewUserId,
                Now,
                Arg.Any<CancellationToken>())
            .Returns(completed);
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.AuthorizeCrewForTripAsync(
                TargetTripId,
                CrewUserId,
                OperatorId,
                "DRIVER",
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(
                TripCrewAuthorizationOutcomeKind.Authorized));
        tripClient.TransferCargoAsync(
                SourceTripId,
                ParcelId,
                TargetTripId,
                "LOADED",
                true,
                requestKey,
                Arg.Any<CancellationToken>())
            .Returns(SuccessfulTransfer());
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var result = await Handler(repository, tripClient, outbox, unitOfWork)
            .Handle(Command(requestKey), CancellationToken.None);

        result.Status.Should().Be("LOADED");
        result.TripId.Should().Be(TargetTripId);
        await unitOfWork.Received(2).BeginTransactionAsync(
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(2).CommitAsync(
            Arg.Any<CancellationToken>());
        await outbox.Received(1).EnqueueAsync(
            Arg.Is<Guid>(eventId => eventId != Guid.Empty),
            ParcelOutboxEvents.TransferConfirmed,
            Arg.Is<string>(payload =>
                payload.Contains(ParcelId.ToString(), StringComparison.OrdinalIgnoreCase)
                && payload.Contains(TargetTripId.ToString(), StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistingClaim_WithNewHttpKey_ReplaysPersistedClaimAndUnknownOutcomeRetainsIt()
    {
        var persistedClaim = Guid.NewGuid();
        var newHttpKey = Guid.NewGuid();
        var claimed = Snapshot(
            claimId: persistedClaim,
            claimedAt: Now.AddMinutes(-6),
            claimedByUserId: CrewUserId);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetTransferConfirmationSnapshotAsync(
                ParcelId,
                Arg.Any<CancellationToken>())
            .Returns(claimed);
        var tripClient = AuthorizedTripClient();
        tripClient.TransferCargoAsync(
                SourceTripId,
                ParcelId,
                TargetTripId,
                "LOADED",
                true,
                persistedClaim,
                Arg.Any<CancellationToken>())
            .Returns(new TripCargoTransferOutcome(
                TripCargoTransferOutcomeKind.TransportError,
                "timeout"));

        var act = () => Handler(
                repository,
                tripClient,
                Substitute.For<IIntegrationEventOutbox>(),
                Substitute.For<IUnitOfWork>())
            .Handle(Command(newHttpKey), CancellationToken.None);

        await act.Should().ThrowAsync<ParcelDependencyUnavailableException>()
            .Where(exception => exception.ErrorCode == "TRIP_SERVICE_UNAVAILABLE");
        await repository.DidNotReceiveWithAnyArgs()
            .TryClearTransferConfirmationClaimAsync(
                default,
                default,
                default,
                default);
        await repository.DidNotReceiveWithAnyArgs()
            .TryClaimTransferConfirmationAsync(
                default,
                default!,
                default,
                default,
                default,
                default,
                default,
                default);
    }

    [Fact]
    public async Task DefinitiveCapacityRejection_ClearsOnlyPersistedClaim()
    {
        var persistedClaim = Guid.NewGuid();
        var claimed = Snapshot(
            claimId: persistedClaim,
            claimedAt: Now.AddMinutes(-6),
            claimedByUserId: CrewUserId);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetTransferConfirmationSnapshotAsync(
                ParcelId,
                Arg.Any<CancellationToken>())
            .Returns(claimed);
        repository.TryClearTransferConfirmationClaimAsync(
                ParcelId,
                persistedClaim,
                Now,
                Arg.Any<CancellationToken>())
            .Returns(true);
        var tripClient = AuthorizedTripClient();
        tripClient.TransferCargoAsync(
                SourceTripId,
                ParcelId,
                TargetTripId,
                "LOADED",
                true,
                persistedClaim,
                Arg.Any<CancellationToken>())
            .Returns(new TripCargoTransferOutcome(
                TripCargoTransferOutcomeKind.CapacityExceeded,
                "full"));
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var act = () => Handler(
                repository,
                tripClient,
                Substitute.For<IIntegrationEventOutbox>(),
                unitOfWork)
            .Handle(Command(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<CodedValidationException>()
            .Where(exception =>
                exception.ErrorCode == "TRIP_CARGO_CAPACITY_EXCEEDED");
        await repository.Received(1).TryClearTransferConfirmationClaimAsync(
            ParcelId,
            persistedClaim,
            Now,
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReliabilityForwardingPlannedLeg_RechecksCapacityWithoutOverflow()
    {
        var persistedClaim = Guid.NewGuid();
        var claimed = Snapshot(
            claimId: persistedClaim,
            claimedAt: Now.AddMinutes(-1),
            claimedByUserId: CrewUserId);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetTransferConfirmationSnapshotAsync(ParcelId, Arg.Any<CancellationToken>())
            .Returns(claimed);
        repository.TryClearTransferConfirmationClaimAsync(
                ParcelId,
                persistedClaim,
                Now,
                Arg.Any<CancellationToken>())
            .Returns(true);
        var plannedLeg = ParcelTransitLeg.Create(
            ParcelId,
            TargetTripId,
            OperatorId,
            2,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Wrong station",
            "Expected stop",
            Guid.NewGuid(),
            null);
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.GetTransitLegAsync(ParcelId, TargetTripId, Arg.Any<CancellationToken>())
            .Returns(plannedLeg);
        var tripClient = AuthorizedTripClient();
        tripClient.TransferCargoAsync(
                SourceTripId,
                ParcelId,
                TargetTripId,
                "LOADED",
                false,
                persistedClaim,
                Arg.Any<CancellationToken>())
            .Returns(new TripCargoTransferOutcome(
                TripCargoTransferOutcomeKind.CapacityExceeded,
                "full"));

        var act = () => Handler(
                repository,
                tripClient,
                Substitute.For<IIntegrationEventOutbox>(),
                Substitute.For<IUnitOfWork>(),
                reliability)
            .Handle(Command(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<CodedValidationException>()
            .Where(exception => exception.ErrorCode == "TRIP_CARGO_CAPACITY_EXCEEDED");
        await tripClient.Received(1).TransferCargoAsync(
            SourceTripId,
            ParcelId,
            TargetTripId,
            "LOADED",
            false,
            persistedClaim,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnclaimedAtExactThirtyMinuteDeadline_TimeoutWins()
    {
        var pending = Snapshot(requestedAt: Now.AddMinutes(-30));
        var repository = Substitute.For<IParcelRepository>();
        repository.GetTransferConfirmationSnapshotAsync(
                ParcelId,
                Arg.Any<CancellationToken>())
            .Returns(pending);
        var tripClient = AuthorizedTripClient();

        var act = () => Handler(
                repository,
                tripClient,
                Substitute.For<IIntegrationEventOutbox>(),
                Substitute.For<IUnitOfWork>())
            .Handle(Command(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<CodedConflictException>()
            .Where(exception =>
                exception.ErrorCode
                    == "PARCEL_TRANSFER_CONFIRMATION_DEADLINE_PASSED");
        await tripClient.DidNotReceiveWithAnyArgs().TransferCargoAsync(
            default,
            default,
            default,
            default!,
            default,
            default,
            default);
    }

    [Fact]
    public async Task CompletedSameClaimReplay_ReturnsPersistedResponseWithoutTripOrOutbox()
    {
        var claimId = Guid.NewGuid();
        var completed = Snapshot(
            status: ParcelStatus.LOADED,
            sourceTripId: TargetTripId,
            claimId: claimId,
            claimedAt: Now.AddMinutes(-1),
            claimedByUserId: CrewUserId,
            confirmedAt: Now,
            confirmedByUserId: CrewUserId);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetTransferConfirmationSnapshotAsync(
                ParcelId,
                Arg.Any<CancellationToken>())
            .Returns(completed);
        var tripClient = AuthorizedTripClient();
        var outbox = Substitute.For<IIntegrationEventOutbox>();

        var result = await Handler(
                repository,
                tripClient,
                outbox,
                Substitute.For<IUnitOfWork>())
            .Handle(Command(claimId), CancellationToken.None);

        result.Status.Should().Be("LOADED");
        await tripClient.DidNotReceiveWithAnyArgs().TransferCargoAsync(
            default,
            default,
            default,
            default!,
            default,
            default,
            default);
        await outbox.DidNotReceiveWithAnyArgs().EnqueueAsync(
            default,
            default!,
            default!,
            default);
    }

    [Fact]
    public async Task CrewEndpoint_DerivesActorOperatorRoleAndRequestKey()
    {
        var requestKey = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(
                Arg.Any<ConfirmTransferCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new OperationalParcelResponse(
                ParcelId,
                ParcelCode,
                "LOADED",
                TargetTripId));
        var controller = new CrewParcelsController(mediator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim("sub", CrewUserId.ToString()),
                        new Claim("operatorId", OperatorId.ToString()),
                        new Claim("role", "ASSISTANT"),
                    ], "test")),
                },
            },
        };
        controller.Request.Headers["Idempotency-Key"] = requestKey.ToString();

        var result = await controller.ConfirmTransferAsync(
            ParcelId,
            new ConfirmParcelTransferRequest(ParcelCode),
            CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        await mediator.Received(1).Send(
            Arg.Is<ConfirmTransferCommand>(command =>
                command.ParcelId == ParcelId
                && command.ParcelCode == ParcelCode
                && command.ConfirmedByUserId == CrewUserId
                && command.OperatorId == OperatorId
                && command.Role == "ASSISTANT"
                && command.IdempotencyKey == requestKey
                && command.ExpectedTargetTripId == null
                && command.RequireCrewAuthorization),
            Arg.Any<CancellationToken>());
    }

    private static ConfirmTransferCommandHandler Handler(
        IParcelRepository repository,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IParcelReliabilityRepository? reliability = null)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new ConfirmTransferCommandHandler(
            repository,
            tripClient,
            outbox,
            unitOfWork,
            clock,
            reliability);
    }

    private static ConfirmTransferCommand Command(Guid idempotencyKey)
        => new(
            ParcelId,
            ParcelCode,
            CrewUserId,
            idempotencyKey,
            OperatorId,
            "DRIVER");

    private static ITripServiceClient AuthorizedTripClient()
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.AuthorizeCrewForTripAsync(
                TargetTripId,
                CrewUserId,
                OperatorId,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(
                TripCrewAuthorizationOutcomeKind.Authorized));
        return tripClient;
    }

    private static TripCargoTransferOutcome SuccessfulTransfer()
        => new(
            TripCargoTransferOutcomeKind.Success,
            Transfer: new TripCargoTransferSnapshot(
                ParcelId,
                SourceTripId,
                TargetTripId,
                "LOADED",
                5m,
                0.01m));

    private static ParcelTransferConfirmationSnapshot Snapshot(
        ParcelStatus status = ParcelStatus.PENDING_TRANSFER_CONFIRM,
        Guid? sourceTripId = null,
        DateTimeOffset? requestedAt = null,
        Guid? claimId = null,
        DateTimeOffset? claimedAt = null,
        Guid? claimedByUserId = null,
        DateTimeOffset? confirmedAt = null,
        Guid? confirmedByUserId = null)
        => new(
            ParcelId,
            ParcelCode,
            OperatorId,
            sourceTripId ?? SourceTripId,
            status,
            TargetTripId,
            requestedAt ?? Now.AddMinutes(-10),
            claimId,
            claimedAt,
            claimedByUserId,
            confirmedAt,
            confirmedByUserId,
            SenderUserId);
}
