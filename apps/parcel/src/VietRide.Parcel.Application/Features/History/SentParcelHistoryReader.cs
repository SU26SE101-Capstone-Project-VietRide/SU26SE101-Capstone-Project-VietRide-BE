using System.Collections.Concurrent;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.History;

public sealed class SentParcelHistoryReader
{
    private const int MaxTripEnrichmentConcurrency = 4;
    private readonly IParcelRepository _parcels;
    private readonly ITripServiceClient _trips;

    public SentParcelHistoryReader(IParcelRepository parcels, ITripServiceClient trips)
    {
        _parcels = parcels;
        _trips = trips;
    }

    public async Task<PagedResult<SentParcelHistoryItemDto>> ReadAsync(
        Guid userId,
        string? statusValue,
        string? fromValue,
        string? toValue,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var range = ParcelHistoryDateRange.Parse(fromValue, toValue);
        var status = statusValue is null
            ? (ParcelStatus?)null
            : Enum.Parse<ParcelStatus>(statusValue, true);
        var result = await _parcels.ListSentByUserIdAsync(
            userId,
            status,
            range.From,
            range.To,
            page,
            pageSize,
            cancellationToken);
        var snapshots = await LoadTripSnapshotsAsync(
            result.Items.Select(parcel => parcel.TripId).Distinct(),
            cancellationToken);

        var items = result.Items.Select(parcel =>
        {
            snapshots.TryGetValue(parcel.TripId, out var trip);
            return new SentParcelHistoryItemDto(
                parcel.Id,
                parcel.ParcelCode,
                parcel.TripId,
                parcel.Status.ToString(),
                parcel.CreatedAt,
                parcel.TotalPrice.Amount,
                trip?.OriginStation.Name,
                trip?.DestinationStation.Name,
                trip?.DepartureDateTime,
                trip?.EstimatedArrivalTime,
                parcel.BookingId,
                parcel.RecipientName,
                parcel.SizeCategory.ToString(),
                parcel.PhotoUrl,
                parcel.DeliveryMethod.ToString());
        }).ToList();

        return PagedResult<SentParcelHistoryItemDto>.Create(
            items,
            result.Page,
            result.PageSize,
            result.TotalItems);
    }

    private async Task<IReadOnlyDictionary<Guid, TripParcelSnapshot>> LoadTripSnapshotsAsync(
        IEnumerable<Guid> tripIds,
        CancellationToken cancellationToken)
    {
        var snapshots = new ConcurrentDictionary<Guid, TripParcelSnapshot>();
        using var gate = new SemaphoreSlim(MaxTripEnrichmentConcurrency);
        var tasks = tripIds.Select(async tripId =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var outcome = await _trips.GetTripParcelSnapshotAsync(tripId, cancellationToken);
                if (outcome.Kind == TripSnapshotOutcomeKind.Success && outcome.Snapshot is not null)
                    snapshots.TryAdd(tripId, outcome.Snapshot);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Journey enrichment is best-effort; local sent history remains authoritative.
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return snapshots;
    }
}
