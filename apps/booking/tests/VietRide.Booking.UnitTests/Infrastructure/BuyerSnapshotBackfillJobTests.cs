using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.OperatorBookings.BuyerSnapshots;
using VietRide.Booking.Infrastructure.Jobs;

namespace VietRide.Booking.UnitTests.Infrastructure;

public sealed class BuyerSnapshotBackfillJobTests
{
    [Fact]
    public async Task Run_NoCandidates_DoesNotCallIdentityOrApplyUpdates()
    {
        var repository = Substitute.For<IBookingRepository>();
        repository.ListBuyerSnapshotBackfillCandidatesAsync(100, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BookingBuyerSnapshotCandidate>());
        var identity = Substitute.For<IIdentityUserServiceClient>();

        await new BuyerSnapshotBackfillJob(repository, identity).RunAsync(CancellationToken.None);

        await identity.DidNotReceive().GetUsersAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().ApplyBuyerSnapshotBackfillAsync(
            Arg.Any<IReadOnlyCollection<BookingBuyerSnapshotUpdate>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_DeduplicatesBuyerIdsAndAppliesEveryMatchingBooking()
    {
        var buyerId = Guid.NewGuid();
        var candidates = new[]
        {
            new BookingBuyerSnapshotCandidate(Guid.NewGuid(), buyerId),
            new BookingBuyerSnapshotCandidate(Guid.NewGuid(), buyerId),
        };
        var repository = Substitute.For<IBookingRepository>();
        repository.ListBuyerSnapshotBackfillCandidatesAsync(100, Arg.Any<CancellationToken>())
            .Returns(candidates);
        var identity = Substitute.For<IIdentityUserServiceClient>();
        identity.GetUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, BookingBuyerSnapshotProfile>
            {
                [buyerId] = Profile(buyerId),
            });

        await new BuyerSnapshotBackfillJob(repository, identity).RunAsync(CancellationToken.None);

        await identity.Received(1).GetUsersAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { buyerId })),
            Arg.Any<CancellationToken>());
        await repository.Received(1).ApplyBuyerSnapshotBackfillAsync(
            Arg.Is<IReadOnlyCollection<BookingBuyerSnapshotUpdate>>(updates =>
                updates.Count == 2
                && updates.All(update => update.Profile.UserId == buyerId)
                && updates.Select(update => update.BookingId).ToHashSet().SetEquals(
                    candidates.Select(candidate => candidate.BookingId))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_RequestsRepositoryBatchOfAtMostOneHundred()
    {
        var repository = Substitute.For<IBookingRepository>();
        repository.ListBuyerSnapshotBackfillCandidatesAsync(100, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BookingBuyerSnapshotCandidate>());

        await new BuyerSnapshotBackfillJob(
            repository,
            Substitute.For<IIdentityUserServiceClient>()).RunAsync(CancellationToken.None);

        await repository.Received(1).ListBuyerSnapshotBackfillCandidatesAsync(
            100,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_ReplayAfterSuccessfulBackfillIsNoOp()
    {
        var buyerId = Guid.NewGuid();
        var candidate = new BookingBuyerSnapshotCandidate(Guid.NewGuid(), buyerId);
        var repository = Substitute.For<IBookingRepository>();
        var calls = 0;
        repository.ListBuyerSnapshotBackfillCandidatesAsync(100, Arg.Any<CancellationToken>())
            .Returns(_ => calls++ == 0
                ? new[] { candidate }
                : Array.Empty<BookingBuyerSnapshotCandidate>());
        var identity = Substitute.For<IIdentityUserServiceClient>();
        identity.GetUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, BookingBuyerSnapshotProfile>
            {
                [buyerId] = Profile(buyerId),
            });

        var job = new BuyerSnapshotBackfillJob(repository, identity);
        await job.RunAsync(CancellationToken.None);
        await job.RunAsync(CancellationToken.None);

        await identity.Received(1).GetUsersAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
        await repository.Received(1).ApplyBuyerSnapshotBackfillAsync(
            Arg.Any<IReadOnlyCollection<BookingBuyerSnapshotUpdate>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_UnresolvedOldestBuyerIsTombstonedSoLaterRowsCannotStarve()
    {
        var unresolvedBuyerId = Guid.NewGuid();
        var candidate = new BookingBuyerSnapshotCandidate(Guid.NewGuid(), unresolvedBuyerId);
        var repository = Substitute.For<IBookingRepository>();
        repository.ListBuyerSnapshotBackfillCandidatesAsync(100, Arg.Any<CancellationToken>())
            .Returns(new[] { candidate });
        var identity = Substitute.For<IIdentityUserServiceClient>();
        identity.GetUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, BookingBuyerSnapshotProfile>());

        await new BuyerSnapshotBackfillJob(repository, identity).RunAsync(CancellationToken.None);

        await repository.Received(1).ApplyBuyerSnapshotBackfillAsync(
            Arg.Is<IReadOnlyCollection<BookingBuyerSnapshotUpdate>>(updates =>
                updates.Count == 1
                && updates.Single().BookingId == candidate.BookingId
                && updates.Single().Profile.UserId == unresolvedBuyerId
                && updates.Single().Profile.Deleted
                && updates.Single().Profile.DisplayName == BookingBuyerSnapshotProfile.DeletedDisplayName
                && updates.Single().Profile.Phone == null
                && updates.Single().Profile.Email == null
                && updates.Single().Profile.AvatarUrl == null),
            Arg.Any<CancellationToken>());
    }

    private static BookingBuyerSnapshotProfile Profile(Guid userId)
        => new(
            userId,
            "Buyer Name",
            "0900000000",
            "buyer@example.test",
            "https://example.test/avatar.jpg",
            false);
}
