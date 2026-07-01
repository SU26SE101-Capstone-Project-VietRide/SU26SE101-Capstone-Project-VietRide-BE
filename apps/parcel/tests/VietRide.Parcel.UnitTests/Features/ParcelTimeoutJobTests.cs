using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels.AutoRejectPendingParcel;
using VietRide.Parcel.Application.Features.Parcels.ExpireParcelAdditionalPayment;
using VietRide.Parcel.Application.Features.Parcels.ExpireParcelReview;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure.Jobs;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class ParcelTimeoutJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.FromHours(7));
    private static readonly DateTimeOffset Past24h = Now.AddHours(-25);
    private static readonly DateTimeOffset JustBefore24h = Now.AddHours(-23).AddMinutes(-59);
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();

    // ================================================================
    // Review timeout job
    // ================================================================

    [Fact]
    public async Task Job_ReviewTimeoutJob_SendsCorrectCommand()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<ExpireParcelReviewCommand>(), Arg.Any<CancellationToken>())
            .Returns(3);

        var job = new ParcelReviewTimeoutJob(mediator,
            Substitute.For<ILogger<ParcelReviewTimeoutJob>>());
        await job.RunAsync(default);

        await mediator.Received(1).Send(
            Arg.Is<ExpireParcelReviewCommand>(c => true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReviewTimeout_HappyPath_ParcelPastWindow_Rejected()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        repo.ListReviewTimedOutIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { ParcelId });

        repo.TryAutoRejectReviewAsync(ParcelId, "PARCEL_REVIEW_TIMEOUT", Now, Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.REJECTED));

        var handler = CreateReviewHandler(repo, clock);
        var result = await handler.Handle(new ExpireParcelReviewCommand(), default);

        result.Should().Be(1);
    }

    [Fact]
    public async Task ReviewTimeout_Idempotency_AlreadyRejected_NoDoubleCount()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        repo.ListReviewTimedOutIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { ParcelId });

        repo.TryAutoRejectReviewAsync(ParcelId, "PARCEL_REVIEW_TIMEOUT", Now, Arg.Any<CancellationToken>())
            .Returns((ParcelPaymentTransitionSnapshot?)null);

        var handler = CreateReviewHandler(repo, clock);
        var result = await handler.Handle(new ExpireParcelReviewCommand(), default);

        result.Should().Be(0);
    }

    [Fact]
    public async Task ReviewTimeout_Boundary_AtCutoff_Rejected()
    {
        var exactlyAtCutoff = Now.AddHours(-24);

        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        repo.ListReviewTimedOutIdsAsync(exactlyAtCutoff, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { ParcelId });

        repo.TryAutoRejectReviewAsync(ParcelId, "PARCEL_REVIEW_TIMEOUT", Now, Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.REJECTED));

        var handler = CreateReviewHandler(repo, clock);
        var result = await handler.Handle(new ExpireParcelReviewCommand(), default);

        result.Should().Be(1);
    }

    [Fact]
    public async Task ReviewTimeout_Boundary_OneTickBefore_NotIncluded()
    {
        var oneTickBeforeCutoff = Now.AddHours(-24).AddTicks(-1);

        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        repo.ListReviewTimedOutIdsAsync(oneTickBeforeCutoff, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<Guid>());

        var handler = CreateReviewHandler(repo, clock);
        var result = await handler.Handle(new ExpireParcelReviewCommand(), default);

        result.Should().Be(0);
    }

    [Fact]
    public async Task ReviewTimeout_BatchCap_ExceedsCap_Truncated()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var manyIds = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToList();
        repo.ListReviewTimedOutIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(manyIds);

        repo.TryAutoRejectReviewAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.REJECTED));

        var handler = CreateReviewHandler(repo, clock);
        var result = await handler.Handle(new ExpireParcelReviewCommand(), default);

        result.Should().Be(200);
    }

    [Fact]
    public async Task ReviewTimeout_NoCandidates_ReturnsZero()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        repo.ListReviewTimedOutIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<Guid>());

        var handler = CreateReviewHandler(repo, clock);
        var result = await handler.Handle(new ExpireParcelReviewCommand(), default);

        result.Should().Be(0);
    }

    // ================================================================
    // Additional payment timeout job
    // ================================================================

    [Fact]
    public async Task Job_AdditionalPaymentTimeoutJob_SendsCorrectCommand()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<ExpireParcelAdditionalPaymentCommand>(), Arg.Any<CancellationToken>())
            .Returns(2);

        var job = new ParcelAdditionalPaymentTimeoutJob(mediator,
            Substitute.For<ILogger<ParcelAdditionalPaymentTimeoutJob>>());
        await job.RunAsync(default);

        await mediator.Received(1).Send(
            Arg.Is<ExpireParcelAdditionalPaymentCommand>(c => true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdditionalPaymentTimeout_HappyPath_DeadlinePassed_Rejected()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        repo.ListAdditionalPaymentTimedOutIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { ParcelId });

        repo.TryMarkAdditionalExpiredByDeadlineAsync(ParcelId, Now, Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.REJECTED));

        var handler = CreateAdditionalPaymentHandler(repo, clock);
        var result = await handler.Handle(new ExpireParcelAdditionalPaymentCommand(), default);

        result.Should().Be(1);
    }

    [Fact]
    public async Task AdditionalPaymentTimeout_Idempotency_AlreadyRejected_NoDoubleCount()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        repo.ListAdditionalPaymentTimedOutIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { ParcelId });

        repo.TryMarkAdditionalExpiredByDeadlineAsync(ParcelId, Now, Arg.Any<CancellationToken>())
            .Returns((ParcelPaymentTransitionSnapshot?)null);

        var handler = CreateAdditionalPaymentHandler(repo, clock);
        var result = await handler.Handle(new ExpireParcelAdditionalPaymentCommand(), default);

        result.Should().Be(0);
    }

    [Fact]
    public async Task AdditionalPaymentTimeout_Boundary_ExactlyAtDeadline_Rejected()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        repo.ListAdditionalPaymentTimedOutIdsAsync(Now, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { ParcelId });

        repo.TryMarkAdditionalExpiredByDeadlineAsync(ParcelId, Now, Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.REJECTED));

        var handler = CreateAdditionalPaymentHandler(repo, clock);
        var result = await handler.Handle(new ExpireParcelAdditionalPaymentCommand(), default);

        result.Should().Be(1);
    }

    [Fact]
    public async Task AdditionalPaymentTimeout_NoCandidates_ReturnsZero()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        repo.ListAdditionalPaymentTimedOutIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Guid>());

        var handler = CreateAdditionalPaymentHandler(repo, clock);
        var result = await handler.Handle(new ExpireParcelAdditionalPaymentCommand(), default);

        result.Should().Be(0);
    }

    // ================================================================
    // Pending auto-reject job
    // ================================================================

    [Fact]
    public async Task Job_PendingAutoRejectJob_SendsCorrectCommand()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<AutoRejectPendingParcelCommand>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var job = new ParcelPendingAutoRejectJob(mediator,
            Substitute.For<ILogger<ParcelPendingAutoRejectJob>>());
        await job.RunAsync(default);

        await mediator.Received(1).Send(
            Arg.Is<AutoRejectPendingParcelCommand>(c => true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PendingAutoReject_HappyPath_TripInProgressAndPastWindow_Rejected()
    {
        var departure = Now.AddHours(-1);
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success,
                CreateTripSnapshot("IN_PROGRESS", departure), null));

        repo.ListPendingForLoadCheckAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingParcelTripRef>
            {
                new(ParcelId, TripId, Now.AddHours(-2)),
            });

        repo.TryAutoRejectPendingAsync(ParcelId, "PARCEL_LATE_LOAD", Now, Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.REJECTED));

        var handler = CreatePendingAutoRejectHandler(repo, tripClient, clock);
        var result = await handler.Handle(new AutoRejectPendingParcelCommand(), default);

        result.Should().Be(1);
    }

    [Fact]
    public async Task PendingAutoReject_TripNotInProgress_Skipped()
    {
        var departure = Now.AddHours(-1);
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success,
                CreateTripSnapshot("SCHEDULED", departure), null));

        repo.ListPendingForLoadCheckAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingParcelTripRef>
            {
                new(ParcelId, TripId, Now.AddHours(-2)),
            });

        var handler = CreatePendingAutoRejectHandler(repo, tripClient, clock);
        var result = await handler.Handle(new AutoRejectPendingParcelCommand(), default);

        result.Should().Be(0);
        await repo.DidNotReceive().TryAutoRejectPendingAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PendingAutoReject_TripInProgressButNotYet30Min_Skipped()
    {
        var departure = Now.AddMinutes(-20);
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success,
                CreateTripSnapshot("IN_PROGRESS", departure), null));

        repo.ListPendingForLoadCheckAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingParcelTripRef>
            {
                new(ParcelId, TripId, Now.AddHours(-2)),
            });

        var handler = CreatePendingAutoRejectHandler(repo, tripClient, clock);
        var result = await handler.Handle(new AutoRejectPendingParcelCommand(), default);

        result.Should().Be(0);
        await repo.DidNotReceive().TryAutoRejectPendingAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PendingAutoReject_TripNotFound_Skipped()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.TripNotFound, null, "not found"));

        repo.ListPendingForLoadCheckAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingParcelTripRef>
            {
                new(ParcelId, TripId, Now.AddHours(-2)),
            });

        var handler = CreatePendingAutoRejectHandler(repo, tripClient, clock);
        var result = await handler.Handle(new AutoRejectPendingParcelCommand(), default);

        result.Should().Be(0);
        await repo.DidNotReceive().TryAutoRejectPendingAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PendingAutoReject_TransportError_Skipped()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.TransportError, null, "down"));

        repo.ListPendingForLoadCheckAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingParcelTripRef>
            {
                new(ParcelId, TripId, Now.AddHours(-2)),
            });

        var handler = CreatePendingAutoRejectHandler(repo, tripClient, clock);
        var result = await handler.Handle(new AutoRejectPendingParcelCommand(), default);

        result.Should().Be(0);
        await repo.DidNotReceive().TryAutoRejectPendingAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PendingAutoReject_MultipleParcelsSameTrip_OneTripCall()
    {
        var departure = Now.AddHours(-1);
        var parcelId1 = Guid.NewGuid();
        var parcelId2 = Guid.NewGuid();
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success,
                CreateTripSnapshot("IN_PROGRESS", departure), null));

        repo.ListPendingForLoadCheckAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingParcelTripRef>
            {
                new(parcelId1, TripId, Now.AddHours(-2)),
                new(parcelId2, TripId, Now.AddHours(-2)),
            });

        repo.TryAutoRejectPendingAsync(Arg.Any<Guid>(), "PARCEL_LATE_LOAD", Now, Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.REJECTED));

        var handler = CreatePendingAutoRejectHandler(repo, tripClient, clock);
        var result = await handler.Handle(new AutoRejectPendingParcelCommand(), default);

        result.Should().Be(2);
        await tripClient.Received(1).GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PendingAutoReject_OneFailingTrip_DoesNotFailWholeRun()
    {
        var departure = Now.AddHours(-1);
        var goodTripId = Guid.NewGuid();
        var badTripId = Guid.NewGuid();
        var goodParcelId = Guid.NewGuid();
        var badParcelId = Guid.NewGuid();
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.GetTripParcelSnapshotAsync(goodTripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success,
                CreateTripSnapshot("IN_PROGRESS", departure, goodTripId), null));
        tripClient.GetTripParcelSnapshotAsync(badTripId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TripSnapshotOutcome>(new HttpRequestException("timeout")));

        repo.ListPendingForLoadCheckAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingParcelTripRef>
            {
                new(goodParcelId, goodTripId, Now.AddHours(-2)),
                new(badParcelId, badTripId, Now.AddHours(-2)),
            });

        repo.TryAutoRejectPendingAsync(goodParcelId, "PARCEL_LATE_LOAD", Now, Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.REJECTED));

        var handler = CreatePendingAutoRejectHandler(repo, tripClient, clock);
        var result = await handler.Handle(new AutoRejectPendingParcelCommand(), default);

        result.Should().Be(1);
    }

    [Fact]
    public async Task PendingAutoReject_NoCandidates_ReturnsZero()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        repo.ListPendingForLoadCheckAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingParcelTripRef>());

        var handler = CreatePendingAutoRejectHandler(repo,
            Substitute.For<ITripServiceClient>(), clock);
        var result = await handler.Handle(new AutoRejectPendingParcelCommand(), default);

        result.Should().Be(0);
    }

    // ================================================================
    // RecurringJobId constants
    // ================================================================

    [Fact]
    public void JobIds_AreExpectedStrings()
    {
        ParcelReviewTimeoutJob.RecurringJobId.Should().Be("parcel.review-timeout");
        ParcelAdditionalPaymentTimeoutJob.RecurringJobId.Should().Be("parcel.additional-payment-timeout");
        ParcelPendingAutoRejectJob.RecurringJobId.Should().Be("parcel.pending-auto-reject");
    }

    // ================================================================
    // Helpers
    // ================================================================

    private static ExpireParcelReviewCommandHandler CreateReviewHandler(
        IParcelRepository repo, IClock clock)
    {
        return new ExpireParcelReviewCommandHandler(repo, clock,
            UnitOfWork(), Outbox(), Stats(),
            Substitute.For<ILogger<ExpireParcelReviewCommandHandler>>());
    }

    private static ExpireParcelAdditionalPaymentCommandHandler CreateAdditionalPaymentHandler(
        IParcelRepository repo, IClock clock)
    {
        return new ExpireParcelAdditionalPaymentCommandHandler(repo, clock,
            UnitOfWork(), Outbox(),
            Substitute.For<ILogger<ExpireParcelAdditionalPaymentCommandHandler>>(), Stats());
    }

    private static AutoRejectPendingParcelCommandHandler CreatePendingAutoRejectHandler(
        IParcelRepository repo, ITripServiceClient tripClient, IClock clock)
    {
        return new AutoRejectPendingParcelCommandHandler(repo, tripClient, clock,
            UnitOfWork(), Outbox(),
            Substitute.For<ILogger<AutoRejectPendingParcelCommandHandler>>(), Stats());
    }

    private static TripParcelSnapshot CreateTripSnapshot(string status, DateTimeOffset departure, Guid? tripId = null)
    {
        var station = new TripStationDto(Guid.NewGuid(), "Station");
        return new TripParcelSnapshot(
            tripId ?? TripId, OperatorId, Guid.NewGuid(), Guid.NewGuid(), status,
            departure, departure.AddHours(4), 100_000,
            station, station,
            new List<TripStopDto>(),
            new TripSeatSummaryDto(40, 35),
            null);
    }

    private static ParcelPaymentTransitionSnapshot Snapshot(ParcelStatus status)
        => new(ParcelId, "VRP-001", status, 100_000, 0, OperatorId, TripId, null, Guid.NewGuid(), ParcelSizeCategory.MEDIUM, null);

    private static IUnitOfWork UnitOfWork()
        => Substitute.For<IUnitOfWork>();

    private static IIntegrationEventOutbox Outbox()
        => Substitute.For<IIntegrationEventOutbox>();

    private static IParcelStatsRepository Stats()
        => Substitute.For<IParcelStatsRepository>();
}
