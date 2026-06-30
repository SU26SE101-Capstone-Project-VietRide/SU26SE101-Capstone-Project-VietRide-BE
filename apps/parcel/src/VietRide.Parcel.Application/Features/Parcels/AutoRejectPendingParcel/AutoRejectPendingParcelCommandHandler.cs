using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.AutoRejectPendingParcel;

public sealed class AutoRejectPendingParcelCommandHandler
    : IRequestHandler<AutoRejectPendingParcelCommand, int>
{
    private static readonly TimeSpan LateLoadWindow = TimeSpan.FromMinutes(30);

    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IClock _clock;
    private readonly ILogger<AutoRejectPendingParcelCommandHandler> _logger;

    public AutoRejectPendingParcelCommandHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient,
        IClock clock,
        ILogger<AutoRejectPendingParcelCommandHandler> logger)
    {
        _parcelRepository = parcelRepository;
        _tripClient = tripClient;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> Handle(
        AutoRejectPendingParcelCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var candidates = await _parcelRepository.ListPendingForLoadCheckAsync(
            MaxBatch, cancellationToken);

        if (candidates.Count == 0)
            return 0;

        if (candidates.Count >= MaxBatch)
        {
            _logger.LogWarning(
                "Pending auto-reject scan hit batch cap of {MaxBatch}. "
                + "Some overdue parcels will be processed on the next tick.",
                MaxBatch);
        }

        // Group by TripId so we call Trip service once per distinct trip
        var byTrip = candidates
            .GroupBy(c => c.TripId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var tripCache = new Dictionary<Guid, TripSnapshotOutcome>();
        var rejectedCount = 0;
        var skippedCount = 0;

        foreach (var (tripId, parcelRefs) in byTrip)
        {
            TripSnapshotOutcome outcome;
            if (!tripCache.TryGetValue(tripId, out outcome!))
            {
                try
                {
                    outcome = await _tripClient.GetTripParcelSnapshotAsync(tripId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Trip service call failed for trip {TripId}. Skipping {ParcelCount} parcel(s) this run.",
                        tripId, parcelRefs.Count);
                    skippedCount += parcelRefs.Count;
                    continue;
                }
                tripCache[tripId] = outcome;
            }

            if (outcome.Kind != TripSnapshotOutcomeKind.Success || outcome.Snapshot is null)
            {
                _logger.LogWarning(
                    "Trip {TripId} unresolved (kind {OutcomeKind}). Skipping {ParcelCount} parcel(s) this run.",
                    tripId, outcome.Kind, parcelRefs.Count);
                skippedCount += parcelRefs.Count;
                continue;
            }

            if (outcome.Snapshot.Status != "IN_PROGRESS")
                continue;

            var cutoff = outcome.Snapshot.DepartureDateTime + LateLoadWindow;

            foreach (var parcelRef in parcelRefs)
            {
                if (now < cutoff)
                    continue;

                var ok = await _parcelRepository.TryAutoRejectPendingAsync(
                    parcelRef.ParcelId, ParcelRejectionReasons.LateLoad, now, cancellationToken);
                if (ok)
                    rejectedCount++;
            }
        }

        if (rejectedCount > 0 || skippedCount > 0)
        {
            _logger.LogInformation(
                "Pending auto-reject scan: rejected {RejectedCount}, skipped {SkippedCount} (trip unresolved).",
                rejectedCount, skippedCount);
        }

        return rejectedCount;
    }

    private const int MaxBatch = 200;
}
