using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Internal.Reports.PlatformParcels;
using VietRide.Parcel.Application.Features.Parcels.DisplaySnapshots;
using VietRide.Parcel.Application.Features.Parcels.OperatorDetail;
using VietRide.Parcel.Application.Features.Parcels.Reports;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Infrastructure.Persistence.Repositories;

internal sealed class ParcelRepository : IParcelRepository
{
    private readonly ParcelDbContext _db;
    private readonly ILogger<ParcelRepository> _logger;

    public ParcelRepository(ParcelDbContext db)
        : this(db, NullLogger<ParcelRepository>.Instance)
    {
    }

    public ParcelRepository(
        ParcelDbContext db,
        ILogger<ParcelRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ParcelTripDisplaySnapshotCandidate>> ListTripDisplaySnapshotBackfillCandidatesAsync(
        int batchSize,
        CancellationToken ct = default)
        => await _db.Parcels
            .AsNoTracking()
            .Where(parcel => parcel.TripSnapshotRouteId == null
                || parcel.TripSnapshotRouteName == null
                || parcel.TripSnapshotOriginStationName == null
                || parcel.TripSnapshotDestinationStationName == null
                || parcel.TripSnapshotVehicleId == null
                || parcel.TripSnapshotVehicleLicensePlate == null)
            .OrderBy(parcel => parcel.CreatedAt)
            .ThenBy(parcel => parcel.Id)
            .Take(Math.Clamp(batchSize, 1, 100))
            .Select(parcel => new ParcelTripDisplaySnapshotCandidate(parcel.Id, parcel.TripId))
            .ToArrayAsync(ct);

    public async Task<int> ApplyTripDisplaySnapshotBackfillAsync(
        IReadOnlyCollection<ParcelTripDisplaySnapshotUpdate> updates,
        CancellationToken ct = default)
    {
        if (updates.Count == 0)
            return 0;

        var normalized = updates
            .ToDictionary(update => update.ParcelId)
            .Values
            .Select(update =>
            {
                if (update.Summary.TripId != update.ExpectedTripId)
                {
                    throw new ArgumentException(
                        "Trip summary id must match the expected Parcel trip id.",
                        nameof(updates));
                }
                if (update.Summary.Route.RouteId == Guid.Empty
                    || update.Summary.Vehicle.VehicleId == Guid.Empty)
                {
                    throw new ArgumentException(
                        "Trip summary route and vehicle ids are required.",
                        nameof(updates));
                }

                return update with
                {
                    Summary = update.Summary with
                    {
                        Route = update.Summary.Route with
                        {
                            Name = NormalizeRequired(update.Summary.Route.Name),
                            OriginName = NormalizeRequired(update.Summary.Route.OriginName),
                            DestinationName = NormalizeRequired(update.Summary.Route.DestinationName),
                        },
                        Vehicle = update.Summary.Vehicle with
                        {
                            LicensePlate = NormalizeRequired(update.Summary.Vehicle.LicensePlate),
                        },
                    },
                };
            })
            .ToArray();

        var parcelIds = normalized.Select(update => update.ParcelId).ToArray();
        var tripIds = normalized.Select(update => update.ExpectedTripId).ToArray();
        var routeIds = normalized.Select(update => update.Summary.Route.RouteId).ToArray();
        var routeNames = normalized.Select(update => update.Summary.Route.Name).ToArray();
        var originNames = normalized.Select(update => update.Summary.Route.OriginName).ToArray();
        var destinationNames = normalized.Select(update => update.Summary.Route.DestinationName).ToArray();
        var vehicleIds = normalized.Select(update => update.Summary.Vehicle.VehicleId).ToArray();
        var licensePlates = normalized.Select(update => update.Summary.Vehicle.LicensePlate).ToArray();

        return await _db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH snapshot_updates (
                parcel_id,
                trip_id,
                route_id,
                route_name,
                origin_name,
                destination_name,
                vehicle_id,
                license_plate) AS (
                SELECT *
                FROM unnest(
                    {parcelIds}::uuid[],
                    {tripIds}::uuid[],
                    {routeIds}::uuid[],
                    {routeNames}::text[],
                    {originNames}::text[],
                    {destinationNames}::text[],
                    {vehicleIds}::uuid[],
                    {licensePlates}::text[])
            )
            UPDATE vietride_parcel.parcels AS parcel
            SET trip_snapshot_route_id = snapshot.route_id,
                trip_snapshot_route_name = snapshot.route_name,
                trip_snapshot_origin_station_name = snapshot.origin_name,
                trip_snapshot_destination_station_name = snapshot.destination_name,
                trip_snapshot_vehicle_id = snapshot.vehicle_id,
                trip_snapshot_vehicle_license_plate = snapshot.license_plate,
                updated_at = CURRENT_TIMESTAMP
            FROM snapshot_updates AS snapshot
            WHERE parcel.id = snapshot.parcel_id
              AND parcel.trip_id = snapshot.trip_id
              AND (
                   parcel.trip_snapshot_route_id IS NULL
                OR parcel.trip_snapshot_route_name IS NULL
                OR parcel.trip_snapshot_origin_station_name IS NULL
                OR parcel.trip_snapshot_destination_station_name IS NULL
                OR parcel.trip_snapshot_vehicle_id IS NULL
                OR parcel.trip_snapshot_vehicle_license_plate IS NULL)
            """, ct);
    }

    public async IAsyncEnumerable<ParcelOperatorReportRow> StreamOperatorReportRowsAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var rows = _db.Parcels
            .AsNoTracking()
            .Where(parcel => parcel.OperatorId == operatorId
                && parcel.CreatedAt >= fromUtc
                && parcel.CreatedAt < toUtc)
            .OrderBy(parcel => parcel.CreatedAt)
            .ThenBy(parcel => parcel.Id)
            .Select(parcel => new ParcelOperatorReportRow(
                parcel.Id,
                parcel.ParcelCode,
                parcel.TripId,
                parcel.Status.ToString(),
                parcel.SizeCategory.ToString(),
                parcel.TotalPrice.Amount,
                parcel.DepositAmount.Amount,
                parcel.AdditionalAmount.Amount,
                parcel.RefundAmount.Amount,
                parcel.CreatedAt,
                parcel.ConfirmedAt));

        await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(ct).ConfigureAwait(false))
        {
            yield return row;
        }
    }

    public async Task<ParcelEntity?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Parcels.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<ParcelEntity> AddAsync(ParcelEntity entity, CancellationToken ct)
    {
        await _db.Parcels.AddAsync(entity, ct);
        return entity;
    }

    public void Update(ParcelEntity entity)
        => _db.Parcels.Update(entity);

    public void Remove(ParcelEntity entity)
        => _db.Parcels.Remove(entity);

    public IQueryable<ParcelEntity> Query()
        => _db.Parcels;

    public IQueryable<ParcelEntity> QueryNoTracking()
        => _db.Parcels.AsNoTracking();

    public async Task<IReadOnlyList<PlatformParcelReportItem>> GetPlatformParcelMetricsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            WITH live AS (
                SELECT operator_id,
                       COUNT(*)::numeric AS delivered_parcel_count,
                       COALESCE(SUM(
                           deposit_amount::numeric
                           + additional_amount::numeric
                           - refund_amount::numeric), 0)::numeric AS parcel_revenue_vnd
                FROM vietride_parcel.parcels
                WHERE status = 'DELIVERY_CONFIRMED'::vietride_parcel.parcel_status
                  AND confirmed_at >= @from_utc
                  AND confirmed_at < @to_utc
                GROUP BY operator_id
            ),
            projected AS (
                SELECT operator_id,
                       COUNT(*)::numeric AS delivered_parcel_count,
                       COALESCE(SUM(parcel_revenue_vnd), 0)::numeric AS parcel_revenue_vnd
                FROM vietride_parcel.platform_parcel_stats
                WHERE confirmed_at >= @from_utc
                  AND confirmed_at < @to_utc
                GROUP BY operator_id
            )
            SELECT COALESCE(live.operator_id, projected.operator_id) AS operator_id,
                   COALESCE(live.delivered_parcel_count, 0)::numeric AS live_count,
                   COALESCE(live.parcel_revenue_vnd, 0)::numeric AS live_revenue,
                   COALESCE(projected.delivered_parcel_count, 0)::numeric AS projected_count,
                   COALESCE(projected.parcel_revenue_vnd, 0)::numeric AS projected_revenue
            FROM live
            FULL OUTER JOIN projected USING (operator_id)
            ORDER BY operator_id;
            """;
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        AddParameter(command, "from_utc", fromUtc.ToUniversalTime());
        AddParameter(command, "to_utc", toUtc.ToUniversalTime());

        var items = new List<PlatformParcelReportItem>();
        long totalCount = 0;
        long totalRevenue = 0;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var deliveredParcelCount = checked((long)reader.GetDecimal(1));
                var parcelRevenueVnd = checked((long)reader.GetDecimal(2));
                var projectedCount = checked((long)reader.GetDecimal(3));
                var projectedRevenue = checked((long)reader.GetDecimal(4));
                var operatorId = reader.GetGuid(0);
                if (deliveredParcelCount != projectedCount
                    || parcelRevenueVnd != projectedRevenue)
                {
                    _logger.LogError(
                        "Platform ParcelStats mismatch for operator {OperatorId}: live count {LiveCount}, projected count {ProjectedCount}, live revenue {LiveRevenueVnd}, projected revenue {ProjectedRevenueVnd}",
                        operatorId,
                        deliveredParcelCount,
                        projectedCount,
                        parcelRevenueVnd,
                        projectedRevenue);
                    throw new PlatformParcelStatsMismatchException();
                }

                totalCount = checked(totalCount + deliveredParcelCount);
                totalRevenue = checked(totalRevenue + parcelRevenueVnd);
                items.Add(new PlatformParcelReportItem(
                    operatorId,
                    deliveredParcelCount,
                    parcelRevenueVnd));
            }
        }
        catch (OverflowException exception)
        {
            throw new PlatformReportValueOverflowException(exception);
        }

        return items;
    }

    public async Task<ParcelEntity?> FindByParcelCodeAsync(string parcelCode, CancellationToken ct = default)
        => await _db.Parcels.FirstOrDefaultAsync(p => p.ParcelCode == parcelCode, ct);

    // ---- Payment deposit transitions (PENDING_PAYMENT) ----

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkDepositSucceededAsync(
        Guid parcelId, Guid paymentId, long depositAmount, DateTimeOffset now, CancellationToken ct)
    {
        var expectedDepositAmount = Money.FromRaw(depositAmount);
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_PAYMENT
                && p.DepositPaymentId == paymentId
                && p.DepositRequiredVnd == expectedDepositAmount)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.RESERVED)
                .SetProperty(p => p.DepositPaidVnd, expectedDepositAmount)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<bool> TryAssignDepositPaymentIdAsync(
        Guid parcelId, Guid paymentId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_PAYMENT
                && (p.DepositPaymentId == null || p.DepositPaymentId == paymentId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.DepositPaymentId, paymentId)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryActivateZeroDepositAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_PAYMENT
                && p.DepositRequiredVnd == Money.Zero)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.RESERVED)
                .SetProperty(p => p.DepositPaidVnd, Money.Zero)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0
            ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct))
            : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryReconcileExpiredDepositAsync(
        Guid parcelId,
        Guid paymentId,
        long amount,
        bool canStillServe,
        Money refundDue,
        string cancellationReason,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var paidAmount = Money.FromRaw(amount);
        var targetStatus = canStillServe ? ParcelStatus.RESERVED : ParcelStatus.CANCELLED;
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.EXPIRED
                && p.DepositRequiredVnd == paidAmount
                && p.DepositPaidVnd == Money.Zero
                && (p.DepositPaymentId == null || p.DepositPaymentId == paymentId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, targetStatus)
                .SetProperty(p => p.DepositPaymentId, paymentId)
                .SetProperty(p => p.DepositPaidVnd, paidAmount)
                .SetProperty(p => p.ForfeitedDepositVnd, Money.Zero)
                .SetProperty(p => p.RefundDueVnd, refundDue)
                .SetProperty(p => p.CancellationReason, canStillServe ? null : cancellationReason)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0
            ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct))
            : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> GetPaymentTransitionSnapshotAsync(
        Guid parcelId, CancellationToken ct)
    {
        var parcel = await _db.Parcels.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parcelId, ct);
        return parcel is null ? null : BuildSnapshot(parcel);
    }

    public async Task<bool> TrySetPendingOperatorActionAsync(
        Guid parcelId,
        PendingActionType actionType,
        string reason,
        Money? refundAmount,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status != ParcelStatus.CANCELLED
                && p.Status != ParcelStatus.REJECTED
                && p.Status != ParcelStatus.EXPIRED
                && p.Status != ParcelStatus.RETURNED
                && p.Status != ParcelStatus.DELIVERY_CONFIRMED)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.PENDING_OPERATOR_ACTION)
                .SetProperty(p => p.PendingActionType, actionType)
                .SetProperty(p => p.PendingActionReason, reason)
                .SetProperty(p => p.RefundAmount, refundAmount ?? Money.Zero)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryResolvePendingOperatorActionAsync(
        Guid parcelId,
        PendingActionType expectedActionType,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_OPERATOR_ACTION
                && p.PendingActionType == expectedActionType)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    p => p.Status,
                    p => p.PendingActionResumeStatus ?? ParcelStatus.PENDING)
                .SetProperty(p => p.PendingActionType, (PendingActionType?)null)
                .SetProperty(p => p.PendingActionResumeStatus, (ParcelStatus?)null)
                .SetProperty(p => p.PendingActionReason, (string?)null)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkDepositFailedAsync(
        Guid parcelId, Guid paymentId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_PAYMENT
                && p.DepositPaymentId == paymentId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.EXPIRED)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkDepositExpiredAsync(
        Guid parcelId, Guid paymentId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_PAYMENT
                && p.DepositPaymentId == paymentId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.EXPIRED)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public Task<bool> ShouldRetainDepositCargoHoldAsync(Guid parcelId, CancellationToken ct)
        => _db.Parcels.AsNoTracking().AnyAsync(parcel =>
            parcel.Id == parcelId
            && ((parcel.Status == ParcelStatus.PENDING_PAYMENT && parcel.DepositPaymentId != null)
                || parcel.Status == ParcelStatus.RESERVED
                || parcel.Status == ParcelStatus.CHECKED_IN
                || parcel.Status == ParcelStatus.PENDING_FINAL_PAYMENT
                || parcel.Status == ParcelStatus.READY_TO_LOAD
                || parcel.Status == ParcelStatus.LOADED
                || parcel.Status == ParcelStatus.IN_TRANSIT), ct);

    // ---- Additional payment transitions (PENDING_ADDITIONAL_PAYMENT) ----

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkAdditionalSucceededAsync(
        Guid parcelId, long additionalAmount, Guid paymentId, DateTimeOffset now, CancellationToken ct)
    {
        var expectedAdditionalAmount = Money.FromRaw(additionalAmount);
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING_ADDITIONAL_PAYMENT && p.AdditionalAmount == expectedAdditionalAmount)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.PENDING)
                .SetProperty(p => p.AdditionalPaymentId, paymentId)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkAdditionalFailedAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING_ADDITIONAL_PAYMENT)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.REJECTED)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkAdditionalExpiredAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING_ADDITIONAL_PAYMENT)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.REJECTED)
                .SetProperty(p => p.RejectionReason, "PARCEL_ADDITIONAL_PAYMENT_TIMEOUT")
                .SetProperty(p => p.RejectedAt, now)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkAdditionalExpiredByDeadlineAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_ADDITIONAL_PAYMENT
                && p.AdditionalPaymentDeadline != null
                && p.AdditionalPaymentDeadline <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.REJECTED)
                .SetProperty(p => p.RejectionReason, "PARCEL_ADDITIONAL_PAYMENT_TIMEOUT")
                .SetProperty(p => p.RejectedAt, now)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    // ---- Operator review transitions (PENDING_OPERATOR_REVIEW) ----

    public async Task<ParcelPaymentTransitionSnapshot?> TryApproveReviewAsync(
        Guid parcelId, Guid reviewedByUserId, Money depositAmount, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_OPERATOR_REVIEW
                && p.ReviewDecision == ParcelReviewDecision.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.PENDING_PAYMENT)
                .SetProperty(p => p.ReviewDecision, ParcelReviewDecision.APPROVED)
                .SetProperty(p => p.ReviewedByUserId, reviewedByUserId)
                .SetProperty(p => p.ReviewedAt, now)
                .SetProperty(p => p.DepositAmount, depositAmount)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryRejectReviewAsync(
        Guid parcelId, Guid reviewedByUserId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_OPERATOR_REVIEW
                && p.ReviewDecision == ParcelReviewDecision.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.REJECTED)
                .SetProperty(p => p.ReviewDecision, ParcelReviewDecision.REJECTED)
                .SetProperty(p => p.ReviewedByUserId, reviewedByUserId)
                .SetProperty(p => p.ReviewedAt, now)
                .SetProperty(p => p.RejectionReason, reason)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryCheckInAsync(
        Guid parcelId,
        Guid tripId,
        string parcelCode,
        Guid checkedInByUserId,
        IReadOnlyCollection<string>? checkInPhotoUrls,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.TripId == tripId
                && p.ParcelCode == parcelCode
                && p.Status == ParcelStatus.RESERVED
                && p.LatestCheckInAt != null
                && p.LatestCheckInAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.CHECKED_IN)
                .SetProperty(p => p.CheckedInAt, now)
                .SetProperty(p => p.CheckedInByUserId, checkedInByUserId)
                .SetProperty(p => p.CheckInPhotoUrls, checkInPhotoUrls)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0
            ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct))
            : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TrySettleReweighAsync(
        Guid parcelId,
        Guid reweighedByUserId,
        decimal actualLengthCm,
        decimal actualWidthCm,
        decimal actualHeightCm,
        decimal actualWeightKg,
        decimal actualVolumeM3,
        decimal actualDimWeightKg,
        decimal actualChargeableWeightKg,
        ParcelSizeCategory actualSizeCategory,
        Money finalGrossPrice,
        Money finalTotalPrice,
        Money balanceRequired,
        Money refundDue,
        DateTimeOffset? finalPaymentDeadline,
        ParcelStatus resumeStatus,
        bool capacityAccepted,
        string? capacityReason,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var query = _db.Parcels.Where(p => p.Id == parcelId && p.Status == ParcelStatus.CHECKED_IN);
        var affected = capacityAccepted
            ? await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, resumeStatus)
                .SetProperty(p => p.PendingActionType, (PendingActionType?)null)
                .SetProperty(p => p.PendingActionResumeStatus, (ParcelStatus?)null)
                .SetProperty(p => p.PendingActionReason, (string?)null)
                .SetProperty(p => p.ActualLengthCm, actualLengthCm)
                .SetProperty(p => p.ActualWidthCm, actualWidthCm)
                .SetProperty(p => p.ActualHeightCm, actualHeightCm)
                .SetProperty(p => p.ActualWeightKg, actualWeightKg)
                .SetProperty(p => p.ActualVolumeM3, actualVolumeM3)
                .SetProperty(p => p.ActualDimWeightKg, actualDimWeightKg)
                .SetProperty(p => p.ActualChargeableWeightKg, actualChargeableWeightKg)
                .SetProperty(p => p.ActualSizeCategory, actualSizeCategory)
                .SetProperty(p => p.FinalGrossPriceVnd, finalGrossPrice)
                .SetProperty(p => p.FinalTotalPriceVnd, finalTotalPrice)
                .SetProperty(p => p.BalanceRequiredVnd, balanceRequired)
                .SetProperty(p => p.RefundDueVnd, refundDue)
                .SetProperty(p => p.FinalPaymentDeadline, finalPaymentDeadline)
                .SetProperty(p => p.ReweighedAt, now)
                .SetProperty(p => p.ReweighedByUserId, reweighedByUserId)
                .SetProperty(p => p.SizeCategory, actualSizeCategory)
                .SetProperty(p => p.TotalPrice, finalTotalPrice)
                .SetProperty(p => p.AdditionalAmount, balanceRequired)
                .SetProperty(p => p.RefundAmount, refundDue)
                .SetProperty(p => p.UpdatedAt, now), ct)
            : await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.PENDING_OPERATOR_ACTION)
                .SetProperty(p => p.PendingActionType, PendingActionType.CAPACITY_EXCEEDED)
                .SetProperty(p => p.PendingActionResumeStatus, resumeStatus)
                .SetProperty(p => p.PendingActionReason, capacityReason)
                .SetProperty(p => p.ActualLengthCm, actualLengthCm)
                .SetProperty(p => p.ActualWidthCm, actualWidthCm)
                .SetProperty(p => p.ActualHeightCm, actualHeightCm)
                .SetProperty(p => p.ActualWeightKg, actualWeightKg)
                .SetProperty(p => p.ActualVolumeM3, actualVolumeM3)
                .SetProperty(p => p.ActualDimWeightKg, actualDimWeightKg)
                .SetProperty(p => p.ActualChargeableWeightKg, actualChargeableWeightKg)
                .SetProperty(p => p.ActualSizeCategory, actualSizeCategory)
                .SetProperty(p => p.FinalGrossPriceVnd, finalGrossPrice)
                .SetProperty(p => p.FinalTotalPriceVnd, finalTotalPrice)
                .SetProperty(p => p.BalanceRequiredVnd, balanceRequired)
                .SetProperty(p => p.RefundDueVnd, refundDue)
                .SetProperty(p => p.FinalPaymentDeadline, finalPaymentDeadline)
                .SetProperty(p => p.ReweighedAt, now)
                .SetProperty(p => p.ReweighedByUserId, reweighedByUserId)
                .SetProperty(p => p.SizeCategory, actualSizeCategory)
                .SetProperty(p => p.TotalPrice, finalTotalPrice)
                .SetProperty(p => p.AdditionalAmount, balanceRequired)
                .SetProperty(p => p.RefundAmount, refundDue)
                .SetProperty(p => p.UpdatedAt, now), ct);

        return affected > 0
            ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct))
            : null;
    }

    public async Task<bool> TryAssignBalancePaymentIdAsync(
        Guid parcelId,
        Guid paymentId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_FINAL_PAYMENT
                && (p.BalancePaymentId == null || p.BalancePaymentId == paymentId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.BalancePaymentId, paymentId)
                .SetProperty(p => p.AdditionalPaymentId, paymentId)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkBalanceSucceededAsync(
        Guid parcelId,
        Guid paymentId,
        long amount,
        DateTimeOffset paidAt,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var paidAmount = Money.FromRaw(amount);
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_FINAL_PAYMENT
                && p.FinalPaymentDeadline != null
                && paidAt < p.FinalPaymentDeadline
                && p.BalanceRequiredVnd == paidAmount
                && p.BalancePaidVnd == Money.Zero
                && (p.BalancePaymentId == null || p.BalancePaymentId == paymentId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.READY_TO_LOAD)
                .SetProperty(p => p.BalancePaymentId, paymentId)
                .SetProperty(p => p.BalancePaidVnd, paidAmount)
                .SetProperty(p => p.ForfeitedDepositVnd, Money.Zero)
                .SetProperty(p => p.AdditionalPaymentId, paymentId)
                .SetProperty(p => p.AdditionalAmount, paidAmount)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0
            ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct))
            : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryReconcileTimedOutBalanceAsync(
        Guid parcelId,
        Guid paymentId,
        long amount,
        DateTimeOffset paidAt,
        bool canStillServe,
        Money refundDue,
        string cancellationReason,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var paidAmount = Money.FromRaw(amount);
        var targetStatus = canStillServe ? ParcelStatus.READY_TO_LOAD : ParcelStatus.CANCELLED;
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.REJECTED
                && p.RejectionReason == "FINAL_PAYMENT_TIMEOUT"
                && p.FinalPaymentDeadline != null
                && paidAt < p.FinalPaymentDeadline
                && p.BalanceRequiredVnd == paidAmount
                && p.BalancePaidVnd == Money.Zero
                && (p.BalancePaymentId == null || p.BalancePaymentId == paymentId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, targetStatus)
                .SetProperty(p => p.BalancePaymentId, paymentId)
                .SetProperty(p => p.BalancePaidVnd, paidAmount)
                .SetProperty(p => p.ForfeitedDepositVnd, Money.Zero)
                .SetProperty(p => p.RefundDueVnd, refundDue)
                .SetProperty(p => p.RejectionReason, (string?)null)
                .SetProperty(p => p.CancellationReason, canStillServe ? null : cancellationReason)
                .SetProperty(p => p.AdditionalPaymentId, paymentId)
                .SetProperty(p => p.AdditionalAmount, paidAmount)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0
            ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct))
            : null;
    }

    public async Task<bool> TryRecordRefundedAmountAsync(
        Guid parcelId,
        Money expectedCurrentAmount,
        Money newRefundedAmount,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.RefundedAmountVnd == expectedCurrentAmount
                && p.RefundDueVnd >= newRefundedAmount)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.RefundedAmountVnd, newRefundedAmount)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0;
    }

    // ---- Reweigh transitions (PENDING) ----

    public async Task<ParcelPaymentTransitionSnapshot?> TryReweighNoFeeAsync(
        Guid parcelId,
        decimal actualLengthCm,
        decimal actualWidthCm,
        decimal actualHeightCm,
        decimal actualWeightKg,
        decimal actualVolumeM3,
        decimal actualDimWeightKg,
        decimal actualChargeableWeightKg,
        ParcelSizeCategory actualSizeCategory,
        Money totalPrice,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.ActualLengthCm, actualLengthCm)
                .SetProperty(p => p.ActualWidthCm, actualWidthCm)
                .SetProperty(p => p.ActualHeightCm, actualHeightCm)
                .SetProperty(p => p.ActualWeightKg, actualWeightKg)
                .SetProperty(p => p.ActualVolumeM3, actualVolumeM3)
                .SetProperty(p => p.ActualDimWeightKg, actualDimWeightKg)
                .SetProperty(p => p.ActualChargeableWeightKg, actualChargeableWeightKg)
                .SetProperty(p => p.SizeCategory, actualSizeCategory)
                .SetProperty(p => p.TotalPrice, totalPrice)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryReweighWithFeeAsync(
        Guid parcelId,
        decimal actualLengthCm,
        decimal actualWidthCm,
        decimal actualHeightCm,
        decimal actualWeightKg,
        decimal actualVolumeM3,
        decimal actualDimWeightKg,
        decimal actualChargeableWeightKg,
        ParcelSizeCategory actualSizeCategory,
        Money totalPrice,
        Money additionalAmount,
        DateTimeOffset deadline,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.PENDING_ADDITIONAL_PAYMENT)
                .SetProperty(p => p.ActualLengthCm, actualLengthCm)
                .SetProperty(p => p.ActualWidthCm, actualWidthCm)
                .SetProperty(p => p.ActualHeightCm, actualHeightCm)
                .SetProperty(p => p.ActualWeightKg, actualWeightKg)
                .SetProperty(p => p.ActualVolumeM3, actualVolumeM3)
                .SetProperty(p => p.ActualDimWeightKg, actualDimWeightKg)
                .SetProperty(p => p.ActualChargeableWeightKg, actualChargeableWeightKg)
                .SetProperty(p => p.SizeCategory, actualSizeCategory)
                .SetProperty(p => p.TotalPrice, totalPrice)
                .SetProperty(p => p.AdditionalAmount, additionalAmount)
                .SetProperty(p => p.AdditionalPaymentDeadline, deadline)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<bool> TryAssignAdditionalPaymentIdAsync(
        Guid parcelId, Guid paymentId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING_ADDITIONAL_PAYMENT)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.AdditionalPaymentId, paymentId)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0;
    }

    // ---- Hangfire job candidate queries (lightweight projections) ----

    public async Task<IReadOnlyList<Guid>> ListReviewTimedOutIdsAsync(
        DateTimeOffset cutoff, int maxBatch, CancellationToken ct)
    {
        return await _db.Parcels
            .Where(p => p.Status == ParcelStatus.PENDING_OPERATOR_REVIEW
                && p.ReviewDecision == ParcelReviewDecision.PENDING
                && p.CreatedAt <= cutoff)
            .OrderBy(p => p.CreatedAt)
            .Take(maxBatch)
            .Select(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ListAdditionalPaymentTimedOutIdsAsync(
        DateTimeOffset now, int maxBatch, CancellationToken ct)
    {
        return await _db.Parcels
            .Where(p => p.Status == ParcelStatus.PENDING_ADDITIONAL_PAYMENT
                && p.AdditionalPaymentDeadline != null
                && p.AdditionalPaymentDeadline <= now)
            .OrderBy(p => p.AdditionalPaymentDeadline)
            .Take(maxBatch)
            .Select(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ListCheckInTimedOutIdsAsync(
        DateTimeOffset now, int maxBatch, CancellationToken ct)
    {
        return await _db.Parcels
            .Where(p => p.Status == ParcelStatus.RESERVED
                && p.LatestCheckInAt != null
                && p.LatestCheckInAt <= now)
            .OrderBy(p => p.LatestCheckInAt)
            .Take(maxBatch)
            .Select(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ListFinalPaymentTimedOutIdsAsync(
        DateTimeOffset now, int maxBatch, CancellationToken ct)
    {
        return await _db.Parcels
            .Where(p => p.Status == ParcelStatus.PENDING_FINAL_PAYMENT
                && p.FinalPaymentDeadline != null
                && p.FinalPaymentDeadline <= now)
            .OrderBy(p => p.FinalPaymentDeadline)
            .Take(maxBatch)
            .Select(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PendingParcelTripRef>> ListPendingForLoadCheckAsync(
        int maxBatch, CancellationToken ct)
    {
        return await _db.Parcels
            .Where(p => p.Status == ParcelStatus.PENDING)
            .OrderBy(p => p.CreatedAt)
            .Take(maxBatch)
            .Select(p => new PendingParcelTripRef(p.Id, p.TripId, p.CreatedAt))
            .ToListAsync(ct);
    }

    // ---- Hangfire job atomic transitions ----

    public async Task<ParcelPaymentTransitionSnapshot?> TryAutoRejectReviewAsync(
        Guid parcelId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_OPERATOR_REVIEW
                && p.ReviewDecision == ParcelReviewDecision.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.CANCELLED)
                .SetProperty(p => p.ReviewedAt, now)
                .SetProperty(p => p.CancellationReason, reason)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryRejectCheckInTimedOutAsync(
        Guid parcelId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.RESERVED
                && p.LatestCheckInAt != null
                && p.LatestCheckInAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.REJECTED)
                .SetProperty(p => p.RejectionReason, reason)
                .SetProperty(p => p.RejectedAt, now)
                .SetProperty(p => p.ForfeitedDepositVnd, p => p.DepositPaidVnd)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0
            ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct))
            : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryRejectFinalPaymentTimedOutAsync(
        Guid parcelId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_FINAL_PAYMENT
                && p.FinalPaymentDeadline != null
                && p.FinalPaymentDeadline <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.REJECTED)
                .SetProperty(p => p.RejectionReason, reason)
                .SetProperty(p => p.RejectedAt, now)
                .SetProperty(p => p.ForfeitedDepositVnd, p => p.DepositPaidVnd)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0
            ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct))
            : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryAutoRejectPendingAsync(
        Guid parcelId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.REJECTED)
                .SetProperty(p => p.RejectionReason, reason)
                .SetProperty(p => p.RejectedAt, now)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkEscalatePendingTransfersAsync(
        DateTimeOffset cutoff, DateTimeOffset now, int maxBatch, CancellationToken ct)
    {
        return await ExecuteBulkReturningAsync(
            """
            WITH candidates AS (
                SELECT id
                FROM vietride_parcel.parcels
                WHERE status = CAST(@source_status AS vietride_parcel.parcel_status)
                  AND transfer_requested_at IS NOT NULL
                  AND transfer_requested_at <= @cutoff
                  AND transfer_confirmation_claim_id IS NULL
                ORDER BY transfer_requested_at
                LIMIT @max_batch
                FOR UPDATE SKIP LOCKED
            )
            UPDATE vietride_parcel.parcels p
            SET status = CAST(@target_status AS vietride_parcel.parcel_status),
                updated_at = @now
            FROM candidates
            WHERE p.id = candidates.id
              AND p.status = CAST(@source_status AS vietride_parcel.parcel_status)
              AND p.transfer_requested_at <= @cutoff
              AND p.transfer_confirmation_claim_id IS NULL
            RETURNING p.id, p.parcel_code, p.operator_id, p.trip_id, p.status::text, p.deposit_amount, p.additional_amount, p.sender_user_id, p.recipient_user_id;
            """,
            command =>
            {
                AddParameter(command, "source_status", ParcelStatus.PENDING_TRANSFER_CONFIRM.ToString());
                AddParameter(command, "target_status", ParcelStatus.TRANSFER_ESCALATED.ToString());
                AddParameter(command, "cutoff", cutoff);
                AddParameter(command, "now", now);
                AddParameter(command, "max_batch", maxBatch);
            },
            ct);
    }

    public async Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkInitiateReturnForRejectedDeliveriesAsync(
        DateTimeOffset cutoff, DateTimeOffset now, int maxBatch, CancellationToken ct)
    {
        return await ExecuteBulkReturningAsync(
            """
            WITH candidates AS (
                SELECT id
                FROM vietride_parcel.parcels
                WHERE status = CAST(@source_status AS vietride_parcel.parcel_status)
                  AND rejected_at IS NOT NULL
                  AND rejected_at <= @cutoff
                ORDER BY rejected_at
                LIMIT @max_batch
            ),
            revoked_tokens AS (
                UPDATE vietride_parcel.parcel_delivery_tokens token
                SET revoked_at = @now,
                    updated_at = @now
                FROM candidates
                WHERE token.parcel_id = candidates.id
                  AND token.revoked_at IS NULL
            )
            UPDATE vietride_parcel.parcels p
            SET status = CAST(@target_status AS vietride_parcel.parcel_status),
                updated_at = @now
            FROM candidates
            WHERE p.id = candidates.id
            RETURNING p.id, p.parcel_code, p.operator_id, p.trip_id, p.status::text, p.deposit_amount, p.additional_amount, p.sender_user_id, p.recipient_user_id;
            """,
            command =>
            {
                AddParameter(command, "source_status", ParcelStatus.DELIVERY_REJECTED.ToString());
                AddParameter(command, "target_status", ParcelStatus.RETURN_INITIATED.ToString());
                AddParameter(command, "cutoff", cutoff);
                AddParameter(command, "now", now);
                AddParameter(command, "max_batch", maxBatch);
            },
            ct);
    }

    public async Task<IReadOnlyList<ParcelDeliveryReminderSnapshot>> TryBulkClaimDeliveryConfirmationRemindersAsync(
        DateTimeOffset expiredAtCutoff,
        DateTimeOffset reminderCutoff,
        DateTimeOffset now,
        int maxBatch,
        CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            WITH candidates AS (
                SELECT parcel.id, token.expires_at
                FROM vietride_parcel.parcels parcel
                JOIN vietride_parcel.parcel_delivery_tokens token
                  ON token.parcel_id = parcel.id
                 AND token.revoked_at IS NULL
                WHERE parcel.status = CAST(@status AS vietride_parcel.parcel_status)
                  AND token.expires_at <= @expired_at_cutoff
                  AND (parcel.last_reminder_at IS NULL OR parcel.last_reminder_at <= @reminder_cutoff)
                ORDER BY token.expires_at, parcel.id
                LIMIT @max_batch
                FOR UPDATE OF parcel SKIP LOCKED
            )
            UPDATE vietride_parcel.parcels p
            SET last_reminder_at = @now,
                updated_at = @now
            FROM candidates
            WHERE p.id = candidates.id
            RETURNING p.id, p.parcel_code, p.operator_id, p.trip_id, candidates.expires_at;
            """;
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        AddParameter(command, "status", ParcelStatus.DELIVERED_PENDING_CONFIRM.ToString());
        AddParameter(command, "expired_at_cutoff", expiredAtCutoff);
        AddParameter(command, "reminder_cutoff", reminderCutoff);
        AddParameter(command, "now", now);
        AddParameter(command, "max_batch", maxBatch);

        var snapshots = new List<ParcelDeliveryReminderSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            snapshots.Add(new ParcelDeliveryReminderSnapshot(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
        }

        return snapshots;
    }

    public async Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkExpireOrphanPendingPaymentsAsync(
        DateTimeOffset cutoff, DateTimeOffset now, int maxBatch, CancellationToken ct)
    {
        return await ExecuteBulkReturningAsync(
            """
            WITH candidates AS (
                SELECT id
                FROM vietride_parcel.parcels
                WHERE status = CAST(@source_status AS vietride_parcel.parcel_status)
                  AND deposit_payment_id IS NULL
                  AND created_at <= @cutoff
                ORDER BY created_at
                LIMIT @max_batch
                FOR UPDATE SKIP LOCKED
            ),
            expired AS (
                UPDATE vietride_parcel.parcels p
                SET status = CAST(@target_status AS vietride_parcel.parcel_status),
                    rejection_reason = 'PAYMENT_NOT_STARTED_TIMEOUT',
                    rejected_at = @now,
                    updated_at = @now
                FROM candidates
                WHERE p.id = candidates.id
                  AND p.status = CAST(@source_status AS vietride_parcel.parcel_status)
                  AND p.deposit_payment_id IS NULL
                RETURNING p.id, p.parcel_code, p.operator_id, p.trip_id, p.status::text,
                          p.deposit_amount, p.additional_amount, p.sender_user_id,
                          p.recipient_user_id
            ),
            release_operations AS (
                INSERT INTO vietride_parcel.parcel_cargo_recovery_operations
                    (id, parcel_id, operator_id, operation_type, status, source_trip_id,
                     target_trip_id, target_state, actor_user_id, reason, refund_amount_vnd,
                     refund_due_vnd, source_status, is_status_override, claimed_at,
                     created_at, updated_at)
                SELECT expired.id, expired.id, expired.operator_id, 'RELEASE', 'PENDING',
                       expired.trip_id, NULL, NULL, NULL, 'ORPHAN_PENDING_PAYMENT_TIMEOUT',
                       0, 0, @source_status, FALSE, @now, @now, @now
                FROM expired
                ON CONFLICT DO NOTHING
                RETURNING id
            )
            SELECT expired.id, expired.parcel_code, expired.operator_id, expired.trip_id,
                   expired.status, expired.deposit_amount, expired.additional_amount,
                   expired.sender_user_id, expired.recipient_user_id
            FROM expired;
            """,
            command =>
            {
                AddParameter(command, "source_status", ParcelStatus.PENDING_PAYMENT.ToString());
                AddParameter(command, "target_status", ParcelStatus.EXPIRED.ToString());
                AddParameter(command, "cutoff", cutoff);
                AddParameter(command, "now", now);
                AddParameter(command, "max_batch", maxBatch);
            },
            ct);
    }

    // ---- Phase 6: Loading / Unloading ----

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkLoadedAsync(
        Guid parcelId, Guid tripId, string parcelCode, Guid? loadedByUserId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.READY_TO_LOAD
                && p.TripId == tripId
                && p.ParcelCode == parcelCode)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.LOADED)
                .SetProperty(p => p.LoadedAt, now)
                .SetProperty(p => p.LoadedByUserId, loadedByUserId)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkUnloadedAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.IN_TRANSIT)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.UNLOADED)
                .SetProperty(p => p.UnloadedAt, now)
                .SetProperty(p => p.DeliveredPendingConfirmAt, (DateTimeOffset?)null)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkDeliveredPendingConfirmAsync(
        Guid parcelId,
        IReadOnlyCollection<string>? deliveryPhotoUrls,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.UNLOADED)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.DELIVERED_PENDING_CONFIRM)
                .SetProperty(p => p.DeliveredPendingConfirmAt, now)
                .SetProperty(p => p.DeliveryPhotoUrls, deliveryPhotoUrls)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkSetInTransitByTripIdAsync(
        Guid tripId,
        DateTimeOffset actualDepartureTime,
        CancellationToken ct)
    {
        return await ExecuteBulkReturningAsync(
            """
            UPDATE vietride_parcel.parcels
            SET status = CAST(@target_status AS vietride_parcel.parcel_status),
                updated_at = @now
            WHERE trip_id = @trip_id
              AND status = CAST(@source_status AS vietride_parcel.parcel_status)
            RETURNING id, parcel_code, operator_id, trip_id, status::text, deposit_amount, additional_amount, sender_user_id, recipient_user_id;
            """,
            command =>
            {
                AddParameter(command, "target_status", ParcelStatus.IN_TRANSIT.ToString());
                AddParameter(command, "source_status", ParcelStatus.LOADED.ToString());
                AddParameter(command, "trip_id", tripId);
                AddParameter(command, "now", actualDepartureTime.ToUniversalTime());
            },
            ct);
    }

    public async Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkSetPendingOperatorActionByTripIdAsync(Guid tripId, DateTimeOffset now, CancellationToken ct)
    {
        return await ExecuteBulkReturningAsync(
            """
            UPDATE vietride_parcel.parcels
            SET status = CAST(@target_status AS vietride_parcel.parcel_status),
                updated_at = @now
            WHERE trip_id = @trip_id
                  AND status IN (
                      CAST(@loaded_status AS vietride_parcel.parcel_status),
                      CAST(@in_transit_status AS vietride_parcel.parcel_status))
            RETURNING id, parcel_code, operator_id, trip_id, status::text, deposit_amount, additional_amount, sender_user_id, recipient_user_id;
            """,
            command =>
            {
                AddParameter(command, "target_status", ParcelStatus.PENDING_OPERATOR_ACTION.ToString());
                AddParameter(command, "loaded_status", ParcelStatus.LOADED.ToString());
                AddParameter(command, "in_transit_status", ParcelStatus.IN_TRANSIT.ToString());
                AddParameter(command, "trip_id", tripId);
                AddParameter(command, "now", now);
            },
            ct);
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryRequestTransferAsync(
        Guid parcelId, Guid operatorId, Guid targetTripId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.OperatorId == operatorId
                && p.TripId != targetTripId
                && (p.Status == ParcelStatus.LOADED || p.Status == ParcelStatus.IN_TRANSIT))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.PENDING_TRANSFER_CONFIRM)
                .SetProperty(p => p.TransferTargetTripId, targetTripId)
                .SetProperty(p => p.TransferRequestedAt, now)
                .SetProperty(p => p.UpdatedAt, now), ct);

        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryCompleteRecoveryTransferAsync(
        Guid parcelId,
        Guid operatorId,
        Guid sourceTripId,
        Guid targetTripId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.OperatorId == operatorId
                && p.TripId == sourceTripId
                && p.TripId != targetTripId
                && p.Status == ParcelStatus.PENDING_OPERATOR_ACTION)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.TripId, targetTripId)
                .SetProperty(p => p.Status, ParcelStatus.RESERVED)
                .SetProperty(p => p.PendingActionType, (PendingActionType?)null)
                .SetProperty(p => p.PendingActionResumeStatus, (ParcelStatus?)null)
                .SetProperty(p => p.PendingActionReason, (string?)null)
                .SetProperty(p => p.TransferTargetTripId, (Guid?)null)
                .SetProperty(p => p.TransferRequestedAt, (DateTimeOffset?)null)
                .SetProperty(p => p.UpdatedAt, now), ct);

        return affected > 0
            ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct))
            : null;
    }

    public async Task<ParcelTransferConfirmationSnapshot?> GetTransferConfirmationSnapshotAsync(
        Guid parcelId,
        CancellationToken ct)
        => await _db.Parcels
            .AsNoTracking()
            .Where(parcel => parcel.Id == parcelId)
            .Select(parcel => new ParcelTransferConfirmationSnapshot(
                parcel.Id,
                parcel.ParcelCode,
                parcel.OperatorId,
                parcel.TripId,
                parcel.Status,
                parcel.TransferTargetTripId,
                parcel.TransferRequestedAt,
                parcel.TransferConfirmationClaimId,
                parcel.TransferConfirmationClaimedAt,
                parcel.TransferConfirmationClaimedByUserId,
                parcel.TransferConfirmedAt,
                parcel.TransferConfirmedByUserId,
                parcel.SenderUserId))
            .SingleOrDefaultAsync(ct);

    public async Task<ParcelTransferConfirmationSnapshot?> TryClaimTransferConfirmationAsync(
        Guid parcelId,
        string parcelCode,
        Guid sourceTripId,
        Guid targetTripId,
        Guid claimId,
        Guid claimedByUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var requestedAfter = now.Subtract(TimeSpan.FromMinutes(30));
        var affected = await _db.Parcels
            .Where(parcel => parcel.Id == parcelId
                && parcel.Status == ParcelStatus.PENDING_TRANSFER_CONFIRM
                && parcel.TripId == sourceTripId
                && parcel.TransferTargetTripId == targetTripId
                && parcel.ParcelCode == parcelCode
                && parcel.TransferRequestedAt != null
                && parcel.TransferRequestedAt > requestedAfter
                && parcel.TransferConfirmationClaimId == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(parcel => parcel.TransferConfirmationClaimId, claimId)
                .SetProperty(parcel => parcel.TransferConfirmationClaimedAt, now)
                .SetProperty(parcel => parcel.TransferConfirmationClaimedByUserId, claimedByUserId)
                .SetProperty(parcel => parcel.UpdatedAt, now), ct);

        return affected > 0
            ? await GetTransferConfirmationSnapshotAsync(parcelId, ct)
            : null;
    }

    public async Task<ParcelTransferConfirmationSnapshot?> TryCompleteTransferConfirmationAsync(
        Guid parcelId,
        Guid sourceTripId,
        Guid targetTripId,
        Guid claimId,
        Guid confirmedByUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(parcel => parcel.Id == parcelId
                && parcel.Status == ParcelStatus.PENDING_TRANSFER_CONFIRM
                && parcel.TripId == sourceTripId
                && parcel.TransferTargetTripId == targetTripId
                && parcel.TransferConfirmationClaimId == claimId
                && parcel.TransferConfirmationClaimedByUserId == confirmedByUserId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(parcel => parcel.TripId, targetTripId)
                .SetProperty(parcel => parcel.Status, ParcelStatus.LOADED)
                .SetProperty(parcel => parcel.TransferConfirmedAt, now)
                .SetProperty(parcel => parcel.TransferConfirmedByUserId, confirmedByUserId)
                .SetProperty(parcel => parcel.LoadedAt, now)
                .SetProperty(parcel => parcel.UpdatedAt, now), ct);

        return affected > 0
            ? await GetTransferConfirmationSnapshotAsync(parcelId, ct)
            : null;
    }

    public async Task<bool> TryClearTransferConfirmationClaimAsync(
        Guid parcelId,
        Guid claimId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(parcel => parcel.Id == parcelId
                && parcel.Status == ParcelStatus.PENDING_TRANSFER_CONFIRM
                && parcel.TransferConfirmationClaimId == claimId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(parcel => parcel.TransferConfirmationClaimId, (Guid?)null)
                .SetProperty(parcel => parcel.TransferConfirmationClaimedAt, (DateTimeOffset?)null)
                .SetProperty(parcel => parcel.TransferConfirmationClaimedByUserId, (Guid?)null)
                .SetProperty(parcel => parcel.UpdatedAt, now), ct);

        return affected > 0;
    }

    public async Task<IReadOnlyList<ParcelTransferConfirmationSnapshot>> GetStaleTransferConfirmationClaimsAsync(
        DateTimeOffset claimedAtCutoff,
        int maxBatch,
        CancellationToken ct)
        => await _db.Parcels
            .AsNoTracking()
            .Where(parcel =>
                parcel.Status == ParcelStatus.PENDING_TRANSFER_CONFIRM
                && parcel.TransferConfirmationClaimId != null
                && parcel.TransferConfirmationClaimedAt != null
                && parcel.TransferConfirmationClaimedAt <= claimedAtCutoff)
            .OrderBy(parcel => parcel.TransferConfirmationClaimedAt)
            .ThenBy(parcel => parcel.Id)
            .Take(maxBatch)
            .Select(parcel => new ParcelTransferConfirmationSnapshot(
                parcel.Id,
                parcel.ParcelCode,
                parcel.OperatorId,
                parcel.TripId,
                parcel.Status,
                parcel.TransferTargetTripId,
                parcel.TransferRequestedAt,
                parcel.TransferConfirmationClaimId,
                parcel.TransferConfirmationClaimedAt,
                parcel.TransferConfirmationClaimedByUserId,
                parcel.TransferConfirmedAt,
                parcel.TransferConfirmedByUserId,
                parcel.SenderUserId))
            .ToListAsync(ct);

    public Task<ParcelCargoRecoveryOperationSnapshot?> GetCargoRecoveryOperationAsync(
        Guid operationId,
        CancellationToken ct)
        => ProjectCargoRecoveryOperations(
                _db.ParcelCargoRecoveryOperations
                    .AsNoTracking()
                    .Where(operation => operation.Id == operationId))
            .SingleOrDefaultAsync(ct);

    public Task<ParcelCargoRecoveryOperationSnapshot?> GetActiveCargoRecoveryOperationAsync(
        Guid parcelId,
        CancellationToken ct)
        => ProjectCargoRecoveryOperations(
                _db.ParcelCargoRecoveryOperations
                    .AsNoTracking()
                    .Where(operation =>
                        operation.ParcelId == parcelId
                        && operation.Status
                            == ParcelCargoRecoveryOperationStatus.PENDING))
            .SingleOrDefaultAsync(ct);

    public async Task<ParcelCargoRecoveryOperationSnapshot?> TryClaimCargoRecoveryTransferAsync(
        Guid operationId,
        Guid parcelId,
        Guid operatorId,
        Guid targetTripId,
        Guid actorUserId,
        string reason,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_parcel.parcel_cargo_recovery_operations
                (id, parcel_id, operator_id, operation_type, status, source_trip_id,
                 target_trip_id, target_state, actor_user_id, reason, refund_amount_vnd,
                 refund_due_vnd, source_status, is_status_override, claimed_at, created_at,
                 updated_at)
            SELECT {operationId}, parcel.id, parcel.operator_id, 'TRANSFER', 'PENDING',
                   parcel.trip_id, {targetTripId}, 'RESERVED', {actorUserId}, {reason},
                   0, 0, parcel.status::text, FALSE, {now}, {now}, {now}
            FROM vietride_parcel.parcels AS parcel
            WHERE parcel.id = {parcelId}
              AND parcel.operator_id = {operatorId}
              AND parcel.status = 'PENDING_OPERATOR_ACTION'::vietride_parcel.parcel_status
              AND parcel.trip_id <> {targetTripId}
            ON CONFLICT DO NOTHING;
            """, ct);

        return affected > 0
            ? await GetCargoRecoveryOperationAsync(operationId, ct)
            : null;
    }

    public async Task<ParcelCargoRecoveryOperationSnapshot?> TryClaimCargoRecoveryReturnAsync(
        Guid operationId,
        Guid parcelId,
        Guid operatorId,
        Guid actorUserId,
        string reason,
        bool isStatusOverride,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_parcel.parcel_cargo_recovery_operations
                (id, parcel_id, operator_id, operation_type, status, source_trip_id,
                 target_trip_id, target_state, actor_user_id, reason, refund_amount_vnd,
                 refund_due_vnd, source_status, is_status_override, claimed_at, created_at,
                 updated_at)
            SELECT {operationId}, parcel.id, parcel.operator_id, 'RETURN', 'PENDING',
                   parcel.trip_id, NULL, NULL, {actorUserId}, {reason},
                   GREATEST(
                       parcel.deposit_paid_vnd
                       + parcel.balance_paid_vnd
                       - parcel.refunded_amount_vnd,
                       0),
                   GREATEST(
                       parcel.refund_due_vnd,
                       parcel.refunded_amount_vnd
                       + GREATEST(
                           parcel.deposit_paid_vnd
                           + parcel.balance_paid_vnd
                           - parcel.refunded_amount_vnd,
                           0)),
                   parcel.status::text, {isStatusOverride}, {now}, {now}, {now}
            FROM vietride_parcel.parcels AS parcel
            WHERE parcel.id = {parcelId}
              AND parcel.operator_id = {operatorId}
              AND parcel.status IN (
                  'PENDING_OPERATOR_ACTION'::vietride_parcel.parcel_status,
                  'TRANSFER_ESCALATED'::vietride_parcel.parcel_status)
            ON CONFLICT DO NOTHING;
            """, ct);

        return affected > 0
            ? await GetCargoRecoveryOperationAsync(operationId, ct)
            : null;
    }

    public async Task<ParcelCargoRecoveryOperationSnapshot?> TryClaimCargoRecoveryReleaseAsync(
        Guid operationId,
        Guid parcelId,
        Guid sourceTripId,
        string reason,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_parcel.parcel_cargo_recovery_operations
                (id, parcel_id, operator_id, operation_type, status, source_trip_id,
                 target_trip_id, target_state, actor_user_id, reason, refund_amount_vnd,
                 refund_due_vnd, source_status, is_status_override, claimed_at, created_at,
                 updated_at)
            SELECT {operationId}, parcel.id, parcel.operator_id, 'RELEASE', 'PENDING',
                   {sourceTripId}, NULL, NULL, NULL, {reason}, 0, 0,
                   parcel.status::text, FALSE, {now}, {now}, {now}
            FROM vietride_parcel.parcels AS parcel
            WHERE parcel.id = {parcelId}
              AND parcel.trip_id = {sourceTripId}
            ON CONFLICT DO NOTHING;
            """, ct);

        return await GetCargoRecoveryOperationAsync(operationId, ct);
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryCompleteCargoRecoveryTransferAsync(
        Guid operationId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var operation = await _db.ParcelCargoRecoveryOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.Id == operationId
                && item.OperationType == ParcelCargoRecoveryOperationType.TRANSFER
                && item.Status == ParcelCargoRecoveryOperationStatus.PENDING,
                ct);
        if (operation?.TargetTripId is null)
        {
            return null;
        }

        var affected = await _db.Parcels
            .Where(parcel =>
                parcel.Id == operation.ParcelId
                && parcel.OperatorId == operation.OperatorId
                && parcel.TripId == operation.SourceTripId
                && parcel.Status == ParcelStatus.PENDING_OPERATOR_ACTION)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(parcel => parcel.TripId, operation.TargetTripId.Value)
                .SetProperty(parcel => parcel.Status, ParcelStatus.RESERVED)
                .SetProperty(parcel => parcel.PendingActionType, (PendingActionType?)null)
                .SetProperty(parcel => parcel.PendingActionResumeStatus, (ParcelStatus?)null)
                .SetProperty(parcel => parcel.PendingActionReason, (string?)null)
                .SetProperty(parcel => parcel.TransferTargetTripId, (Guid?)null)
                .SetProperty(parcel => parcel.TransferRequestedAt, (DateTimeOffset?)null)
                .SetProperty(parcel => parcel.UpdatedAt, now),
                ct);
        if (affected == 0)
        {
            return null;
        }

        var completed = await _db.ParcelCargoRecoveryOperations
            .Where(item =>
                item.Id == operationId
                && item.Status == ParcelCargoRecoveryOperationStatus.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    item => item.Status,
                    ParcelCargoRecoveryOperationStatus.COMPLETED)
                .SetProperty(item => item.CompletedAt, now)
                .SetProperty(item => item.UpdatedAt, now),
                ct);
        if (completed == 0)
        {
            return null;
        }

        return BuildSnapshot(await _db.Parcels
            .AsNoTracking()
            .SingleAsync(parcel => parcel.Id == operation.ParcelId, ct));
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryCompleteCargoRecoveryReturnAsync(
        Guid operationId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var operation = await _db.ParcelCargoRecoveryOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.Id == operationId
                && item.OperationType == ParcelCargoRecoveryOperationType.RETURN
                && item.Status == ParcelCargoRecoveryOperationStatus.PENDING,
                ct);
        if (operation is null)
        {
            return null;
        }

        var affected = await _db.Parcels
            .Where(parcel =>
                parcel.Id == operation.ParcelId
                && parcel.OperatorId == operation.OperatorId
                && parcel.TripId == operation.SourceTripId
                && (parcel.Status == ParcelStatus.PENDING_OPERATOR_ACTION
                    || parcel.Status == ParcelStatus.TRANSFER_ESCALATED))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(parcel => parcel.Status, ParcelStatus.RETURNED)
                .SetProperty(parcel => parcel.ReturnReason, operation.Reason)
                .SetProperty(parcel => parcel.ReturnedAt, now)
                .SetProperty(parcel => parcel.ReturnedByUserId, operation.ActorUserId)
                .SetProperty(
                    parcel => parcel.RefundDueVnd,
                    Money.FromRaw(operation.RefundDueVnd))
                .SetProperty(parcel => parcel.UpdatedAt, now),
                ct);
        if (affected == 0)
        {
            return null;
        }

        var completed = await _db.ParcelCargoRecoveryOperations
            .Where(item =>
                item.Id == operationId
                && item.Status == ParcelCargoRecoveryOperationStatus.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    item => item.Status,
                    ParcelCargoRecoveryOperationStatus.COMPLETED)
                .SetProperty(item => item.CompletedAt, now)
                .SetProperty(item => item.UpdatedAt, now),
                ct);
        if (completed == 0)
        {
            return null;
        }

        return BuildSnapshot(await _db.Parcels
            .AsNoTracking()
            .SingleAsync(parcel => parcel.Id == operation.ParcelId, ct));
    }

    public async Task<bool> TryCompleteCargoRecoveryReleaseAsync(
        Guid operationId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.ParcelCargoRecoveryOperations
            .Where(operation => operation.Id == operationId
                && operation.OperationType == ParcelCargoRecoveryOperationType.RELEASE
                && operation.Status == ParcelCargoRecoveryOperationStatus.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(operation => operation.Status, ParcelCargoRecoveryOperationStatus.COMPLETED)
                .SetProperty(operation => operation.CompletedAt, now)
                .SetProperty(operation => operation.UpdatedAt, now), ct);
        return affected > 0;
    }

    public async Task<bool> TryFailCargoRecoveryOperationAsync(
        Guid operationId,
        string failureCode,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.ParcelCargoRecoveryOperations
            .Where(operation =>
                operation.Id == operationId
                && operation.Status == ParcelCargoRecoveryOperationStatus.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    operation => operation.Status,
                    ParcelCargoRecoveryOperationStatus.FAILED)
                .SetProperty(operation => operation.CompletedAt, now)
                .SetProperty(operation => operation.FailureCode, failureCode)
                .SetProperty(operation => operation.UpdatedAt, now),
                ct);
        return affected > 0;
    }

    public async Task<IReadOnlyList<ParcelCargoRecoveryOperationSnapshot>>
        GetStaleCargoRecoveryOperationsAsync(
            DateTimeOffset claimedAtCutoff,
            int maxBatch,
            CancellationToken ct)
    {
        var operations = _db.ParcelCargoRecoveryOperations
            .AsNoTracking()
            .Where(operation =>
                operation.Status == ParcelCargoRecoveryOperationStatus.PENDING
                && operation.ClaimedAt <= claimedAtCutoff)
            .OrderBy(operation => operation.ClaimedAt)
            .ThenBy(operation => operation.Id)
            .Take(maxBatch);

        return await ProjectCargoRecoveryOperations(operations)
            .ToListAsync(ct);
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryReturnAsync(
        Guid parcelId,
        Guid operatorId,
        Guid returnedByUserId,
        string reason,
        long refundDueVnd,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.OperatorId == operatorId
                && (p.Status == ParcelStatus.PENDING_OPERATOR_ACTION || p.Status == ParcelStatus.TRANSFER_ESCALATED))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.RETURNED)
                .SetProperty(p => p.ReturnReason, reason)
                .SetProperty(p => p.ReturnedAt, now)
                .SetProperty(p => p.ReturnedByUserId, returnedByUserId)
                .SetProperty(p => p.RefundDueVnd, Money.FromRaw(refundDueVnd))
                .SetProperty(p => p.UpdatedAt, now), ct);

        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    private IQueryable<ParcelCargoRecoveryOperationSnapshot>
        ProjectCargoRecoveryOperations(
            IQueryable<ParcelCargoRecoveryOperation> operations)
        => from operation in operations
           join parcel in _db.Parcels.AsNoTracking()
               on operation.ParcelId equals parcel.Id
           select new ParcelCargoRecoveryOperationSnapshot(
               operation.Id,
               parcel.Id,
               parcel.ParcelCode,
               operation.OperatorId,
               parcel.SenderUserId,
               operation.OperationType,
               operation.Status,
               operation.SourceTripId,
               operation.TargetTripId,
               operation.TargetState,
               operation.ActorUserId,
               operation.Reason,
               operation.RefundAmountVnd,
               operation.RefundDueVnd,
               operation.SourceStatus,
               operation.IsStatusOverride,
               operation.ClaimedAt,
               operation.CompletedAt,
               operation.FailureCode,
               parcel.ActualWeightKg ?? parcel.EstimatedWeightKg,
               parcel.ActualVolumeM3 ?? parcel.EstimatedVolumeM3,
               parcel.Status,
               parcel.TripId,
               parcel.ReturnedAt);

    public async Task<IReadOnlyList<TripCancellationParcelCandidate>> GetTripCancellationCandidatesAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct)
        => await _db.Parcels
            .AsNoTracking()
            .Where(parcel => parcel.TripId == tripId && parcel.OperatorId == operatorId)
            .OrderBy(parcel => parcel.Id)
            .Select(parcel => new TripCancellationParcelCandidate(
                parcel.Id,
                parcel.ParcelCode,
                parcel.OperatorId,
                parcel.TripId,
                parcel.Status,
                parcel.DepositPaidVnd.Amount,
                parcel.BalancePaidVnd.Amount,
                parcel.RefundedAmountVnd.Amount,
                parcel.SenderUserId,
                parcel.EstimatedWeightKg,
                parcel.EstimatedVolumeM3,
                parcel.ActualWeightKg,
                parcel.ActualVolumeM3))
            .ToListAsync(ct);

    public async Task<bool> TryApplyTripCancellationAsync(
        Guid parcelId,
        Guid operatorId,
        ParcelStatus expectedStatus,
        ParcelStatus targetStatus,
        long refundDueVnd,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var query = _db.Parcels.Where(parcel => parcel.Id == parcelId
            && parcel.OperatorId == operatorId
            && parcel.Status == expectedStatus);

        int affected;
        if (targetStatus == ParcelStatus.CANCELLED)
        {
            affected = await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(parcel => parcel.Status, ParcelStatus.CANCELLED)
                .SetProperty(parcel => parcel.CancellationReason, "TRIP_CANCELLED")
                .SetProperty(parcel => parcel.RefundDueVnd, Money.FromRaw(refundDueVnd))
                .SetProperty(parcel => parcel.UpdatedAt, now), ct);
        }
        else if (targetStatus == ParcelStatus.PENDING_OPERATOR_ACTION)
        {
            affected = await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(parcel => parcel.Status, ParcelStatus.PENDING_OPERATOR_ACTION)
                .SetProperty(parcel => parcel.PendingActionReason, "TRIP_CANCELLED")
                .SetProperty(parcel => parcel.UpdatedAt, now), ct);
        }
        else
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetStatus),
                targetStatus,
                "Trip cancellation supports only CANCELLED or PENDING_OPERATOR_ACTION.");
        }

        return affected == 1;
    }

    public async Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkRequestTransferByTripIdAsync(
        Guid oldTripId,
        Guid newTripId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        return await ExecuteBulkReturningAsync(
            """
            UPDATE vietride_parcel.parcels
            SET status = CAST(@target_status AS vietride_parcel.parcel_status),
                transfer_target_trip_id = @new_trip_id,
                transfer_requested_at = @now,
                transfer_confirmed_at = NULL,
                transfer_confirmed_by_user_id = NULL,
                transfer_confirmation_claim_id = NULL,
                transfer_confirmation_claimed_at = NULL,
                transfer_confirmation_claimed_by_user_id = NULL,
                updated_at = @now
            WHERE trip_id = @old_trip_id
                  AND operator_id = @operator_id
                  AND status IN (
                      CAST(@loaded_status AS vietride_parcel.parcel_status),
                      CAST(@in_transit_status AS vietride_parcel.parcel_status))
            RETURNING id, parcel_code, operator_id, trip_id, status::text, deposit_amount, additional_amount, sender_user_id, recipient_user_id, transfer_target_trip_id;
            """,
            command =>
            {
                AddParameter(command, "target_status", ParcelStatus.PENDING_TRANSFER_CONFIRM.ToString());
                AddParameter(command, "loaded_status", ParcelStatus.LOADED.ToString());
                AddParameter(command, "in_transit_status", ParcelStatus.IN_TRANSIT.ToString());
                AddParameter(command, "old_trip_id", oldTripId);
                AddParameter(command, "new_trip_id", newTripId);
                AddParameter(command, "operator_id", operatorId);
                AddParameter(command, "now", now);
            },
            ct);
    }

    public async Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkRealertPendingOperatorActionAsync(
        DateTimeOffset cutoff,
        DateTimeOffset reminderCutoff,
        DateTimeOffset now,
        int maxBatch,
        CancellationToken ct)
    {
        return await ExecuteBulkReturningAsync(
            """
            WITH candidates AS (
                SELECT id
                FROM vietride_parcel.parcels
                WHERE status = CAST(@status AS vietride_parcel.parcel_status)
                  AND updated_at <= @cutoff
                  AND (last_reminder_at IS NULL OR last_reminder_at <= @reminder_cutoff)
                ORDER BY updated_at
                LIMIT @max_batch
            )
            UPDATE vietride_parcel.parcels p
            SET last_reminder_at = @now,
                updated_at = @now
            FROM candidates
            WHERE p.id = candidates.id
            RETURNING p.id, p.parcel_code, p.operator_id, p.trip_id, p.status::text, p.deposit_amount, p.additional_amount, p.sender_user_id, p.recipient_user_id;
            """,
            command =>
            {
                AddParameter(command, "status", ParcelStatus.PENDING_OPERATOR_ACTION.ToString());
                AddParameter(command, "cutoff", cutoff);
                AddParameter(command, "reminder_cutoff", reminderCutoff);
                AddParameter(command, "now", now);
                AddParameter(command, "max_batch", maxBatch);
            },
            ct);
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryManualCancelAsync(
        Guid parcelId,
        Guid operatorId,
        ParcelStatus targetStatus,
        string reason,
        long refundDueVnd,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (targetStatus != ParcelStatus.CANCELLED)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetStatus),
                targetStatus,
                "Manual cancellation always targets CANCELLED.");
        }

        ParcelStatus[] sourceStatuses =
        [
            ParcelStatus.PENDING_OPERATOR_REVIEW,
            ParcelStatus.PENDING_PAYMENT,
            ParcelStatus.PENDING,
            ParcelStatus.PENDING_ADDITIONAL_PAYMENT,
            ParcelStatus.RESERVED,
            ParcelStatus.CHECKED_IN,
            ParcelStatus.PENDING_FINAL_PAYMENT,
            ParcelStatus.READY_TO_LOAD,
        ];

        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.OperatorId == operatorId
                && sourceStatuses.Contains(p.Status))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, targetStatus)
                .SetProperty(p => p.CancellationReason, reason)
                .SetProperty(p => p.RefundDueVnd, Money.FromRaw(refundDueVnd))
                .SetProperty(p => p.UpdatedAt, now), ct);

        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    // ---- Phase 6: Queries ----

    public async Task<PagedResult<ParcelEntity>> ListReceivedByUserIdAsync(
        Guid userId, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.Parcels
            .Where(p => p.RecipientUserId == userId)
            .OrderByDescending(p => p.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<ParcelEntity>.Create(items, page, pageSize, total);
    }

    public async Task<PagedResult<ParcelEntity>> ListSentByUserIdAsync(
        Guid userId,
        ParcelStatus? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = _db.Parcels
            .AsNoTracking()
            .Where(parcel => parcel.SenderUserId == userId);

        if (status.HasValue)
            query = query.Where(parcel => parcel.Status == status.Value);
        if (from.HasValue)
            query = query.Where(parcel => parcel.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(parcel => parcel.CreatedAt < to.Value);

        var total = await query.LongCountAsync(ct);
        var items = await query
            .OrderByDescending(parcel => parcel.CreatedAt)
            .ThenByDescending(parcel => parcel.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<ParcelEntity>.Create(items, page, pageSize, total);
    }

    public async Task<PagedResult<ParcelEntity>> ListByTripAndOperatorAsync(
        Guid tripId, Guid operatorId, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.Parcels
            .Where(p => p.TripId == tripId && p.OperatorId == operatorId)
            .OrderByDescending(p => p.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<ParcelEntity>.Create(items, page, pageSize, total);
    }

    public async Task<PagedResult<ParcelEntity>> ListByOperatorAsync(
        Guid operatorId,
        ParcelStatus? status,
        Guid? tripId,
        PendingActionType? pendingActionType,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = _db.Parcels
            .AsNoTracking()
            .Where(parcel => parcel.OperatorId == operatorId);

        if (status.HasValue)
            query = query.Where(parcel => parcel.Status == status.Value);
        if (tripId.HasValue)
            query = query.Where(parcel => parcel.TripId == tripId.Value);
        if (pendingActionType.HasValue)
            query = query.Where(parcel => parcel.PendingActionType == pendingActionType.Value);

        var total = await query.LongCountAsync(ct);
        var items = await query
            .OrderByDescending(parcel => parcel.CreatedAt)
            .ThenByDescending(parcel => parcel.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<ParcelEntity>.Create(items, page, pageSize, total);
    }

    public async Task<PagedResult<ParcelEntity>> ListByOperatorFilteredAsync(
        Guid operatorId,
        ParcelStatus? status,
        Guid? tripId,
        PendingActionType? pendingActionType,
        string? search,
        IReadOnlyCollection<Guid> senderUserIds,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtcExclusive,
        string dateField,
        ParcelSizeCategory? sizeCategory,
        Guid? routeId,
        string sortBy,
        string sortDir,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var normalizedSearch = search?.Trim();
        var query = string.IsNullOrEmpty(normalizedSearch)
            ? _db.Parcels.AsNoTracking()
            : _db.Parcels.FromSqlInterpolated($"""
                SELECT parcel.*
                FROM vietride_parcel.parcels AS parcel
                WHERE strpos(lower(unaccent(parcel.parcel_code)), lower(unaccent({normalizedSearch}))) > 0
                   OR strpos(lower(unaccent(parcel.recipient_name)), lower(unaccent({normalizedSearch}))) > 0
                   OR strpos(parcel.recipient_phone, {normalizedSearch}) > 0
                   OR parcel.sender_user_id = ANY ({senderUserIds.ToArray()})
                """).AsNoTracking();
        query = query.Where(parcel => parcel.OperatorId == operatorId);
        if (status.HasValue) query = query.Where(parcel => parcel.Status == status.Value);
        if (tripId.HasValue) query = query.Where(parcel => parcel.TripId == tripId.Value);
        if (pendingActionType.HasValue) query = query.Where(parcel => parcel.PendingActionType == pendingActionType.Value);
        if (sizeCategory.HasValue)
            query = query.Where(parcel => (parcel.ActualSizeCategory ?? parcel.EstimatedSizeCategory) == sizeCategory.Value);
        if (routeId.HasValue) query = query.Where(parcel => parcel.TripSnapshotRouteId == routeId.Value);
        var useDeadline = dateField.Equals("finalPaymentDeadline", StringComparison.OrdinalIgnoreCase);
        if (fromUtc.HasValue)
            query = useDeadline ? query.Where(p => p.FinalPaymentDeadline >= fromUtc) : query.Where(p => p.CreatedAt >= fromUtc);
        if (toUtcExclusive.HasValue)
            query = useDeadline ? query.Where(p => p.FinalPaymentDeadline < toUtcExclusive) : query.Where(p => p.CreatedAt < toUtcExclusive);

        var total = await query.LongCountAsync(ct);
        var descending = sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);
        var ordered = sortBy.Equals("finalPaymentDeadline", StringComparison.OrdinalIgnoreCase)
            ? descending ? query.OrderByDescending(p => p.FinalPaymentDeadline) : query.OrderBy(p => p.FinalPaymentDeadline)
            : descending ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt);
        ordered = descending ? ordered.ThenByDescending(p => p.Id) : ordered.ThenBy(p => p.Id);
        var items = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return PagedResult<ParcelEntity>.Create(items, page, pageSize, total);
    }

    public async Task<OperatorParcelDetailData?> GetOperatorDetailAsync(
        Guid parcelId,
        Guid operatorId,
        CancellationToken ct = default)
        => await _db.Parcels
            .AsNoTracking()
            .Where(parcel => parcel.Id == parcelId && parcel.OperatorId == operatorId)
            .Select(parcel => new OperatorParcelDetailData(
                parcel,
                _db.ParcelStatusHistories
                    .AsNoTracking()
                    .Where(history => history.ParcelId == parcel.Id)
                    .OrderBy(history => history.OccurredAt)
                    .ThenBy(history => history.Id)
                    .ToList()))
            .SingleOrDefaultAsync(ct);

    // ---- Phase 7: Delivery Token ----

    public async Task<ParcelPaymentTransitionSnapshot?> TryConfirmDeliveryAsync(
        Guid parcelId, Guid deliveryTokenId, string ip, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && _db.ParcelDeliveryTokens.Any(token =>
                    token.Id == deliveryTokenId
                    && token.ParcelId == p.Id
                    && token.RevokedAt == null
                    && token.ExpiresAt > now)
                && p.Status == ParcelStatus.DELIVERED_PENDING_CONFIRM)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.DELIVERY_CONFIRMED)
                .SetProperty(p => p.ConfirmedAt, now)
                .SetProperty(p => p.ConfirmedByIp, ip)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryRejectDeliveryAsync(
        Guid parcelId, Guid deliveryTokenId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && _db.ParcelDeliveryTokens.Any(token =>
                    token.Id == deliveryTokenId
                    && token.ParcelId == p.Id
                    && token.RevokedAt == null
                    && token.ExpiresAt > now)
                && p.Status == ParcelStatus.DELIVERED_PENDING_CONFIRM)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.DELIVERY_REJECTED)
                .SetProperty(p => p.RejectedAt, now)
                .SetProperty(p => p.RejectionReason, reason)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryUndoRejectDeliveryAsync(
        Guid parcelId, Guid deliveryTokenId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && _db.ParcelDeliveryTokens.Any(token =>
                    token.Id == deliveryTokenId
                    && token.ParcelId == p.Id
                    && token.RevokedAt == null
                    && token.ExpiresAt > now)
                && p.Status == ParcelStatus.DELIVERY_REJECTED
                && p.RejectedAt != null
                && p.RejectedAt.Value.AddMinutes(15) > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.DELIVERED_PENDING_CONFIRM)
                .SetProperty(p => p.RejectedAt, (DateTimeOffset?)null)
                .SetProperty(p => p.RejectionReason, (string?)null)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryPrepareDeliveryResendAsync(
        Guid parcelId,
        ParcelStatus expectedStatus,
        Guid expectedActiveTokenId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var query = _db.Parcels
            .Where(parcel =>
                parcel.Id == parcelId
                && parcel.Status == expectedStatus
                && _db.ParcelDeliveryTokens.Any(token =>
                    token.Id == expectedActiveTokenId
                    && token.ParcelId == parcel.Id
                    && token.RevokedAt == null));

        if (expectedStatus == ParcelStatus.DELIVERY_REJECTED)
        {
            query = query.Where(parcel =>
                parcel.RejectedAt != null
                && parcel.RejectedAt.Value.AddMinutes(15) > now);
        }
        else if (expectedStatus != ParcelStatus.DELIVERED_PENDING_CONFIRM)
        {
            return null;
        }

        var affected = await query.ExecuteUpdateAsync(setters => setters
            .SetProperty(parcel => parcel.Status, ParcelStatus.DELIVERED_PENDING_CONFIRM)
            .SetProperty(parcel => parcel.RejectedAt, (DateTimeOffset?)null)
            .SetProperty(parcel => parcel.RejectionReason, (string?)null)
            .SetProperty(parcel => parcel.LastReminderAt, (DateTimeOffset?)null)
            .SetProperty(parcel => parcel.UpdatedAt, now), ct);

        return affected > 0
            ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(parcel => parcel.Id == parcelId, ct))
            : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryManualConfirmDeliveryAsync(
        Guid parcelId, Guid operatorId, Guid actorUserId, string note, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.OperatorId == operatorId
                && p.Status == ParcelStatus.DELIVERED_PENDING_CONFIRM)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.DELIVERY_CONFIRMED)
                .SetProperty(p => p.ConfirmedAt, now)
                .SetProperty(p => p.ConfirmedByUserId, actorUserId)
                .SetProperty(p => p.ConfirmNote, note)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public Task<ParcelManualConfirmationSnapshot?> GetManualConfirmationSnapshotAsync(
        Guid parcelId,
        CancellationToken ct)
        => _db.Parcels
            .AsNoTracking()
            .Where(parcel => parcel.Id == parcelId)
            .Select(parcel => new ParcelManualConfirmationSnapshot(
                parcel.Id,
                parcel.Status,
                parcel.ConfirmedAt,
                parcel.ConfirmedByUserId,
                parcel.ConfirmNote))
            .SingleOrDefaultAsync(ct);

    private static ParcelPaymentTransitionSnapshot BuildSnapshot(ParcelEntity p)
        => new(
            p.Id, p.ParcelCode, p.Status, p.DepositAmount.Amount, p.AdditionalAmount.Amount,
            p.OperatorId, p.TripId, p.BookingId, p.SenderUserId, p.SizeCategory, p.AdditionalPaymentId);

    private async Task<IReadOnlyList<ParcelEventSnapshot>> ExecuteBulkReturningAsync(
        string sql,
        Action<System.Data.Common.DbCommand> configure,
        CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        configure(command);

        var snapshots = new List<ParcelEventSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            snapshots.Add(new ParcelEventSnapshot(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                Enum.Parse<ParcelStatus>(reader.GetString(4)),
                reader.FieldCount > 5 ? reader.GetInt64(5) : 0,
                reader.FieldCount > 6 ? reader.GetInt64(6) : 0,
                reader.FieldCount > 7 ? reader.GetGuid(7) : Guid.Empty,
                reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetGuid(8) : null));
        }

        return snapshots;
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string NormalizeRequired(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Snapshot display value is required.", nameof(value));

        return value.Trim();
    }
}
