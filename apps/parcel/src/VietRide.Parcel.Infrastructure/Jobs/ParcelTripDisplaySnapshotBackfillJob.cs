using Hangfire;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels.DisplaySnapshots;

namespace VietRide.Parcel.Infrastructure.Jobs;

public sealed class ParcelTripDisplaySnapshotBackfillJob
{
    public const string RecurringJobId = "parcel.trip-display-snapshot-backfill";
    private const int BatchSize = 100;

    private readonly IParcelRepository _parcels;
    private readonly ITripServiceClient _trips;

    public ParcelTripDisplaySnapshotBackfillJob(
        IParcelRepository parcels,
        ITripServiceClient trips)
    {
        _parcels = parcels;
        _trips = trips;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var candidates = await _parcels.ListTripDisplaySnapshotBackfillCandidatesAsync(
            BatchSize,
            cancellationToken);
        if (candidates.Count == 0)
            return;

        var outcome = await _trips.GetTripSummariesAsync(
            candidates.Select(candidate => candidate.TripId).Distinct().ToArray(),
            cancellationToken);
        if (outcome.Kind != TripSummaryBatchOutcomeKind.Success)
        {
            throw new InvalidOperationException(
                outcome.ErrorMessage ?? "Trip summary batch failed during Parcel snapshot backfill.");
        }

        var summaries = outcome.Summaries.ToDictionary(summary => summary.TripId);
        var updates = candidates
            .Where(candidate => summaries.ContainsKey(candidate.TripId))
            .Select(candidate => new ParcelTripDisplaySnapshotUpdate(
                candidate.ParcelId,
                candidate.TripId,
                summaries[candidate.TripId]))
            .ToArray();
        if (updates.Length > 0)
        {
            await _parcels.ApplyTripDisplaySnapshotBackfillAsync(updates, cancellationToken);
        }
    }
}
