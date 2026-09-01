using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Services;

internal sealed class ShuttleRoutePreviewService : IShuttleRoutePreviewService
{
    private const int ArrivalBufferMinutes = 30;
    private const int DefaultStopServiceMinutes = 5;
    private const string LateRiskWarningCode = "SHUTTLE_LATE_RISK";
    private const string GoongBasis = "GOONG";

    private readonly TripDbContext db;
    private readonly IShuttleRouteEstimator routeEstimator;
    private readonly int stopServiceMinutes;

    public ShuttleRoutePreviewService(
        TripDbContext db,
        IShuttleRouteEstimator routeEstimator,
        IConfiguration configuration)
    {
        this.db = db;
        this.routeEstimator = routeEstimator;
        stopServiceMinutes = int.TryParse(
            configuration["SHUTTLE_STOP_SERVICE_MINUTES"],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configuredMinutes)
            ? Math.Max(0, configuredMinutes)
            : DefaultStopServiceMinutes;
    }

    public async Task<ShuttleRoutePreviewResult> PreviewAsync(
        ShuttleRoutePreviewInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.Direction == ShuttleTrip.OutboundDirection)
        {
            return Result(ShuttleRoutePreviewStatuses.NotApplicable);
        }

        if (input.Direction != ShuttleTrip.InboundDirection)
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Shuttle direction is invalid.");
        }

        var trip = await db.Trips.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == input.MainTripId && item.OperatorId == input.OperatorId,
                cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Main trip was not found.");
        var route = await db.Routes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == trip.RouteId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Main trip route was not found.");
        var station = await db.Stations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == route.OriginStationId, cancellationToken)
            ?? throw new CodedNotFoundException("STATION_NOT_FOUND", "Shuttle station was not found.");

        var selectedIds = input.OrderedBookingIds.Distinct().ToArray();
        var manifests = await db.ShuttlePassengers.AsNoTracking()
            .Where(item => item.MainTripId == input.MainTripId
                && item.Direction == input.Direction
                && item.BookingId.HasValue
                && selectedIds.Contains(item.BookingId.Value))
            .OrderBy(item => item.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var groups = manifests
            .GroupBy(item => item.BookingId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (input.OrderedBookingIds.Any(bookingId =>
                !groups.TryGetValue(bookingId, out var group)
                || group.Length == 0
                || group.Any(item => item.Status != ShuttlePassenger.PendingAssignmentStatus)))
        {
            throw new CodedConflictException(
                "SHUTTLE_REQUEST_SET_CHANGED",
                "One or more selected Booking groups changed.");
        }

        var hardCutoffAt = trip.DepartureDateTime.AddMinutes(-ArrivalBufferMinutes);
        if (!station.Latitude.HasValue || !station.Longitude.HasValue)
        {
            return Result(ShuttleRoutePreviewStatuses.Unknown, hardCutoffAt: hardCutoffAt);
        }

        var orderedCoordinates = input.OrderedBookingIds
            .Select(bookingId => groups[bookingId][0])
            .Select(manifest => new ShuttleRouteCoordinate(manifest.PickupLat, manifest.PickupLng))
            .ToArray();
        var routeDuration = await routeEstimator.EstimateDurationAsync(
            orderedCoordinates[0],
            orderedCoordinates.Skip(1)
                .Append(new ShuttleRouteCoordinate(station.Latitude.Value, station.Longitude.Value))
                .ToArray(),
            cancellationToken);
        if (!routeDuration.HasValue)
        {
            return Result(ShuttleRoutePreviewStatuses.Unknown, hardCutoffAt: hardCutoffAt);
        }

        var estimatedFinishAt = input.ScheduledDepartureTime
            + routeDuration.Value
            + TimeSpan.FromMinutes((long)stopServiceMinutes * input.OrderedBookingIds.Count);
        if (estimatedFinishAt <= hardCutoffAt)
        {
            return Result(
                ShuttleRoutePreviewStatuses.Safe,
                estimatedFinishAt,
                hardCutoffAt,
                delayMinutes: 0,
                basis: GoongBasis);
        }

        var delayMinutes = checked((int)Math.Ceiling((estimatedFinishAt - hardCutoffAt).TotalMinutes));
        return Result(
            ShuttleRoutePreviewStatuses.LateRisk,
            estimatedFinishAt,
            hardCutoffAt,
            delayMinutes,
            LateRiskWarningCode,
            GoongBasis);
    }

    private static ShuttleRoutePreviewResult Result(
        string status,
        DateTimeOffset? estimatedFinishAt = null,
        DateTimeOffset? hardCutoffAt = null,
        int? delayMinutes = null,
        string? warningCode = null,
        string? basis = null) =>
        new(
            status,
            estimatedFinishAt,
            hardCutoffAt,
            delayMinutes,
            warningCode,
            LateRiskBlocksCreate: false,
            basis);
}
