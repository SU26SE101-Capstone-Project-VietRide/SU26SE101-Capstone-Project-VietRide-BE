using Hangfire;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.OperatorBookings.BuyerSnapshots;

namespace VietRide.Booking.Infrastructure.Jobs;

public sealed class BuyerSnapshotBackfillJob
{
    public const string RecurringJobId = "booking.buyer-snapshot-backfill";
    private const int BatchSize = 100;

    private readonly IBookingRepository bookings;
    private readonly IIdentityUserServiceClient identityUsers;

    public BuyerSnapshotBackfillJob(
        IBookingRepository bookings,
        IIdentityUserServiceClient identityUsers)
    {
        this.bookings = bookings;
        this.identityUsers = identityUsers;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var candidates = await bookings.ListBuyerSnapshotBackfillCandidatesAsync(
            BatchSize,
            cancellationToken);
        if (candidates.Count == 0)
        {
            return;
        }

        var profiles = await identityUsers.GetUsersAsync(
            candidates.Select(candidate => candidate.BuyerUserId).Distinct().ToArray(),
            cancellationToken);
        var updates = candidates
            .Select(candidate => new BookingBuyerSnapshotUpdate(
                candidate.BookingId,
                profiles.TryGetValue(candidate.BuyerUserId, out var profile)
                    ? profile
                    : DeletedProfile(candidate.BuyerUserId)))
            .ToArray();
        await bookings.ApplyBuyerSnapshotBackfillAsync(updates, cancellationToken);
    }

    private static BookingBuyerSnapshotProfile DeletedProfile(Guid buyerUserId)
        => new(
            buyerUserId,
            BookingBuyerSnapshotProfile.DeletedDisplayName,
            null,
            null,
            null,
            true);
}
