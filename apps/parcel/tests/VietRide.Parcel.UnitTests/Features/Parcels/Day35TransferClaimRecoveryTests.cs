using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;
using VietRide.Parcel.Application.Features.Parcels.RecoverTransferClaims;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure.Jobs;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.UnitTests.Features.Parcels;

public sealed class Day35TransferClaimRecoveryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 5, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task StaleClaimRecovery_ReusesPersistedKeyTargetAndActor()
    {
        var claimId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        var targetTripId = Guid.NewGuid();
        var claimedByUserId = Guid.NewGuid();
        var repository = Substitute.For<IParcelRepository>();
        repository.GetStaleTransferConfirmationClaimsAsync(
                Now.AddMinutes(-5),
                100,
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new ParcelTransferConfirmationSnapshot(
                    parcelId,
                    "VRP-RECOVERY",
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    ParcelStatus.PENDING_TRANSFER_CONFIRM,
                    targetTripId,
                    Now.AddMinutes(-20),
                    claimId,
                    Now.AddMinutes(-5),
                    claimedByUserId,
                    null,
                    null,
                    Guid.NewGuid()),
            ]);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(
                Arg.Any<ConfirmTransferCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new OperationalParcelResponse(
                parcelId,
                "VRP-RECOVERY",
                "LOADED",
                targetTripId));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var recovered = await new RecoverTransferClaimsCommandHandler(
                repository,
                mediator,
                clock,
                Substitute.For<ILogger<RecoverTransferClaimsCommandHandler>>())
            .Handle(new RecoverTransferClaimsCommand(), CancellationToken.None);

        recovered.Should().Be(1);
        await mediator.Received(1).Send(
            Arg.Is<ConfirmTransferCommand>(command =>
                command.ParcelId == parcelId
                && command.IdempotencyKey == claimId
                && command.ConfirmedByUserId == claimedByUserId
                && command.ExpectedTargetTripId == targetTripId
                && !command.RequireCrewAuthorization),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RecoveryJob_HasStableFiveMinuteRecurringIdentity()
    {
        PendingTransferClaimRecoveryJob.RecurringJobId.Should()
            .Be("parcel.pending-transfer-claim-recovery");
    }
}
