using System.Collections.Concurrent;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.PassengerHistory;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

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
        var context = await LoadPageAsync(
            userId,
            statusValue,
            fromValue,
            toValue,
            page,
            pageSize,
            cancellationToken);
        var items = context.Page.Items
            .Select(parcel => MapHistoryItem(parcel, context.TripSnapshots))
            .ToList();

        return PagedResult<SentParcelHistoryItemDto>.Create(
            items,
            context.Page.Page,
            context.Page.PageSize,
            context.Page.TotalItems);
    }

    internal async Task<PagedResult<PassengerParcelHistoryProjection>> ReadForPassengerHistoryAsync(
        Guid userId,
        string? statusValue,
        string? fromValue,
        string? toValue,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var context = await LoadPageAsync(
            userId,
            statusValue,
            fromValue,
            toValue,
            page,
            pageSize,
            cancellationToken);
        var items = context.Page.Items.Select(parcel => new PassengerParcelHistoryProjection(
            MapHistoryItem(parcel, context.TripSnapshots),
            parcel.Status,
            parcel.DepositPaymentId,
            parcel.BalancePaymentId,
            RemainingAmount(parcel.DepositRequiredVnd.Amount, parcel.DepositPaidVnd.Amount),
            RemainingAmount(parcel.BalanceRequiredVnd.Amount, parcel.BalancePaidVnd.Amount),
            parcel.LatestCheckInAt,
            parcel.FinalPaymentDeadline,
            parcel.DropoffStopId,
            context.TripSnapshots.GetValueOrDefault(parcel.TripId)?.DestinationStation.Id))
            .ToList();

        return PagedResult<PassengerParcelHistoryProjection>.Create(
            items,
            context.Page.Page,
            context.Page.PageSize,
            context.Page.TotalItems);
    }

    private async Task<SentHistoryReadContext> LoadPageAsync(
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

        return new SentHistoryReadContext(result, snapshots);
    }

    private static SentParcelHistoryItemDto MapHistoryItem(
        ParcelEntity parcel,
        IReadOnlyDictionary<Guid, TripParcelSnapshot> snapshots)
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
    }

    private static long RemainingAmount(long required, long paid)
        => required <= paid ? 0 : required - paid;

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

    private sealed record SentHistoryReadContext(
        PagedResult<ParcelEntity> Page,
        IReadOnlyDictionary<Guid, TripParcelSnapshot> TripSnapshots);
}
