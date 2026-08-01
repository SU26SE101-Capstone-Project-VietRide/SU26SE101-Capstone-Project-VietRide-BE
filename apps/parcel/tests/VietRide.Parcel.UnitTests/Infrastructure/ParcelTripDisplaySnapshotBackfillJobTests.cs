using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels.DisplaySnapshots;
using VietRide.Parcel.Infrastructure.Jobs;

namespace VietRide.Parcel.UnitTests.Infrastructure;

public sealed class ParcelTripDisplaySnapshotBackfillJobTests
{
    [Fact]
    public async Task Run_NoCandidates_DoesNotCallTripOrApplyUpdates()
    {
        var repository = Substitute.For<IParcelRepository>();
        repository.ListTripDisplaySnapshotBackfillCandidatesAsync(100, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelTripDisplaySnapshotCandidate>());
        var trip = Substitute.For<ITripServiceClient>();

        await new ParcelTripDisplaySnapshotBackfillJob(repository, trip)
            .RunAsync(CancellationToken.None);

        await trip.DidNotReceive().GetTripSummariesAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().ApplyTripDisplaySnapshotBackfillAsync(
            Arg.Any<IReadOnlyCollection<ParcelTripDisplaySnapshotUpdate>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_UsesOneDistinctBoundedTripBatchAndAppliesMatchingParcels()
    {
        var tripId = Guid.NewGuid();
        var candidates = new[]
        {
            new ParcelTripDisplaySnapshotCandidate(Guid.NewGuid(), tripId),
            new ParcelTripDisplaySnapshotCandidate(Guid.NewGuid(), tripId),
        };
        var repository = Substitute.For<IParcelRepository>();
        repository.ListTripDisplaySnapshotBackfillCandidatesAsync(100, Arg.Any<CancellationToken>())
            .Returns(candidates);
        var trip = Substitute.For<ITripServiceClient>();
        trip.GetTripSummariesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(TripSummaryBatchOutcome.Success([Summary(tripId)]));

        await new ParcelTripDisplaySnapshotBackfillJob(repository, trip)
            .RunAsync(CancellationToken.None);

        await trip.Received(1).GetTripSummariesAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { tripId })),
            Arg.Any<CancellationToken>());
        await repository.Received(1).ApplyTripDisplaySnapshotBackfillAsync(
            Arg.Is<IReadOnlyCollection<ParcelTripDisplaySnapshotUpdate>>(updates =>
                updates.Count == 2
                && updates.All(update => update.ExpectedTripId == tripId)
                && updates.Select(update => update.ParcelId).ToHashSet().SetEquals(
                    candidates.Select(candidate => candidate.ParcelId))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_RequestsAtMostOneHundredAndSkipsMissingTripIds()
    {
        var foundTripId = Guid.NewGuid();
        var missingTripId = Guid.NewGuid();
        var repository = Substitute.For<IParcelRepository>();
        repository.ListTripDisplaySnapshotBackfillCandidatesAsync(100, Arg.Any<CancellationToken>())
            .Returns(
            [
                new ParcelTripDisplaySnapshotCandidate(Guid.NewGuid(), foundTripId),
                new ParcelTripDisplaySnapshotCandidate(Guid.NewGuid(), missingTripId),
            ]);
        var trip = Substitute.For<ITripServiceClient>();
        trip.GetTripSummariesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(TripSummaryBatchOutcome.Success([Summary(foundTripId)]));

        await new ParcelTripDisplaySnapshotBackfillJob(repository, trip)
            .RunAsync(CancellationToken.None);

        await repository.Received(1).ListTripDisplaySnapshotBackfillCandidatesAsync(
            100,
            Arg.Any<CancellationToken>());
        await repository.Received(1).ApplyTripDisplaySnapshotBackfillAsync(
            Arg.Is<IReadOnlyCollection<ParcelTripDisplaySnapshotUpdate>>(updates =>
                updates.Count == 1 && updates.Single().ExpectedTripId == foundTripId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_TransportFailureThrowsForHangfireRetryWithoutWriting()
    {
        var repository = Substitute.For<IParcelRepository>();
        repository.ListTripDisplaySnapshotBackfillCandidatesAsync(100, Arg.Any<CancellationToken>())
            .Returns([new ParcelTripDisplaySnapshotCandidate(Guid.NewGuid(), Guid.NewGuid())]);
        var trip = Substitute.For<ITripServiceClient>();
        trip.GetTripSummariesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(TripSummaryBatchOutcome.TransportFailure("trip down"));

        var action = () => new ParcelTripDisplaySnapshotBackfillJob(repository, trip)
            .RunAsync(CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        await repository.DidNotReceive().ApplyTripDisplaySnapshotBackfillAsync(
            Arg.Any<IReadOnlyCollection<ParcelTripDisplaySnapshotUpdate>>(),
            Arg.Any<CancellationToken>());
    }

    private static TripSummarySnapshot Summary(Guid tripId)
        => new(
            tripId,
            "COMPLETED",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            new TripRouteSummarySnapshot(Guid.NewGuid(), "Route", "Origin", "Destination"),
            new TripVehicleSummarySnapshot(Guid.NewGuid(), "51B-12345", "ACTIVE"));
}
