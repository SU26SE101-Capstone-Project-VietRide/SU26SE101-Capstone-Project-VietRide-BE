using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Services;

public sealed class TripStopSnapshotSyncService : ITripStopSnapshotSyncService
{
    private const int MaximumConcurrentBookingImpactCalls = 8;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITripRepository trips;
    private readonly ITripSeatRepository tripSeats;
    private readonly ITripStopRepository tripStops;
    private readonly ITripStopFareRepository tripStopFares;
    private readonly ITripAuditLogRepository auditLogs;
    private readonly IBookingImpactClient bookingImpact;

    public TripStopSnapshotSyncService(
        ITripRepository trips,
        ITripSeatRepository tripSeats,
        ITripStopRepository tripStops,
        ITripStopFareRepository tripStopFares,
        ITripAuditLogRepository auditLogs,
        IBookingImpactClient bookingImpact)
    {
        this.trips = trips;
        this.tripSeats = tripSeats;
        this.tripStops = tripStops;
        this.tripStopFares = tripStopFares;
        this.auditLogs = auditLogs;
        this.bookingImpact = bookingImpact;
    }

    public async Task<TripStopSnapshotSyncPreflight> PreflightAsync(
        Guid routeId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidateIds = await trips.QueryNoTracking()
            .Where(trip => trip.RouteId == routeId
                && trip.OperatorId == operatorId
                && trip.Status == TripStatus.SCHEDULED
                && trip.DepartureDateTime > now
                && trip.AlternativeRouteId == null)
            .OrderBy(trip => trip.DepartureDateTime)
            .ThenBy(trip => trip.Id)
            .Select(trip => trip.Id)
            .ToArrayAsync(cancellationToken);

        using var concurrency = new SemaphoreSlim(MaximumConcurrentBookingImpactCalls);
        var checks = candidateIds.Select(async tripId =>
        {
            await concurrency.WaitAsync(cancellationToken);
            try
            {
                var impact = await bookingImpact.GetTripEditImpactAsync(
                    tripId,
                    operatorId,
                    cancellationToken);
                return (TripId: tripId, Eligible: impact.ActiveBookingCount == 0);
            }
            finally
            {
                concurrency.Release();
            }
        });
        var results = await Task.WhenAll(checks);
        return new TripStopSnapshotSyncPreflight(
            routeId,
            operatorId,
            results.Where(result => result.Eligible).Select(result => result.TripId).ToArray());
    }

    public async Task SynchronizeAsync(
        TripStopSnapshotSyncPreflight preflight,
        IReadOnlyList<RouteStop> targetStops,
        Guid actorUserId,
        string sourceMutation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Actor user id cannot be empty.", nameof(actorUserId));
        }

        var targetByStopId = targetStops.ToDictionary(stop => stop.StopId);
        foreach (var tripId in preflight.EligibleTripIds.OrderBy(id => id))
        {
            var trip = await trips.GetForUpdateAsync(tripId, cancellationToken);
            if (trip is null
                || trip.RouteId != preflight.RouteId
                || trip.OperatorId != preflight.OperatorId
                || trip.Status != TripStatus.SCHEDULED
                || trip.DepartureDateTime <= now
                || trip.AlternativeRouteId != null)
            {
                continue;
            }

            var seats = await tripSeats.AcquireForVehicleSwapAsync(trip.Id, cancellationToken);
            if (seats.Any(seat => seat.Status is TripSeatStatus.HELD or TripSeatStatus.BOOKED))
            {
                continue;
            }

            var existingStops = await tripStops.AcquireByTripAsync(trip.Id, cancellationToken);
            var existingFares = await tripStopFares.AcquireByTripAsync(trip.Id, cancellationToken);
            var existingByStopId = existingStops.ToDictionary(stop => stop.StopId);
            var addedStopIds = new List<Guid>();
            var removedStopIds = new List<Guid>();
            var updatedStopIds = new List<Guid>();

            foreach (var existing in existingStops)
            {
                if (!targetByStopId.TryGetValue(existing.StopId, out var target))
                {
                    tripStops.Remove(existing);
                    removedStopIds.Add(existing.StopId);
                    continue;
                }

                if (existing.SynchronizeSnapshot(
                    target.OrderIndex,
                    trip.DepartureDateTime.AddMinutes(target.EstimatedDurationFromOriginMinutes),
                    target.AllowPickup,
                    target.AllowDropoff,
                    target.DistanceFromOriginKm))
                {
                    tripStops.Update(existing);
                    updatedStopIds.Add(existing.StopId);
                }
            }

            foreach (var target in targetStops
                         .Where(stop => !existingByStopId.ContainsKey(stop.StopId))
                         .OrderBy(stop => stop.OrderIndex))
            {
                await tripStops.AddAsync(
                    TripStop.Create(
                        trip.Id,
                        target.StopId,
                        target.OrderIndex,
                        trip.DepartureDateTime.AddMinutes(target.EstimatedDurationFromOriginMinutes),
                        target.AllowPickup,
                        target.AllowDropoff,
                        target.DistanceFromOriginKm),
                    cancellationToken);
                addedStopIds.Add(target.StopId);
            }

            var removedStopIdSet = removedStopIds.ToHashSet();
            tripStopFares.RemoveRange(existingFares.Where(fare => removedStopIdSet.Contains(fare.StopId)));
            if (addedStopIds.Count == 0 && removedStopIds.Count == 0 && updatedStopIds.Count == 0)
            {
                continue;
            }

            await auditLogs.AddAsync(
                TripAuditLog.Create(
                    Guid.NewGuid(),
                    trip.Id,
                    actorUserId,
                    TripAuditAction.TripStopSnapshotSynced,
                    JsonSerializer.Serialize(new
                    {
                        routeId = preflight.RouteId,
                        sourceMutation,
                        addedStopIds,
                        removedStopIds,
                        updatedStopIds,
                    }, JsonOptions),
                    now),
                cancellationToken);
        }
    }
}
