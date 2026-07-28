using VietRide.Booking.Application.Features.Internal.Bookings;
using VietRide.Booking.Application.Features.Internal.Reports.PlatformBookings;
using VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;
using VietRide.Booking.Application.Features.OperatorReports;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Application.Abstractions.Repositories;

/// <summary>
/// Repository contract for the Booking aggregate.
/// Extends <see cref="IRepository{TEntity,TId}"/> with Booking-specific queries.
/// </summary>
public interface IBookingRepository : IRepository<BookingEntity, Guid>
{
    IAsyncEnumerable<BookingOperatorReportRow> StreamOperatorReportRowsAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        bool cancellationOnly,
        CancellationToken ct = default)
        => throw new NotSupportedException("Operator Booking report is not implemented by this repository.");
    Task<PagedResult<BookingEntity>> ListPassengerHistoryAsync(
        Guid passengerUserId,
        BookingStatus? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken ct = default)
        => throw new NotSupportedException("Passenger booking history is not implemented by this repository.");

    Task<IReadOnlyList<PlatformBookingReportItem>> GetPlatformBookingMetricsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
        => throw new NotSupportedException("Platform Booking report is not implemented by this repository.");

    Task AcquireEventLockAsync(Guid sourceEventId, CancellationToken ct = default)
        => throw new NotSupportedException("Booking event lock is not implemented by this repository.");

    Task<IReadOnlyList<BookingEntity>> GetScheduleChangeBookingsForUpdateAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct = default)
        => throw new NotSupportedException("Schedule-change projection lookup is not implemented by this repository.");

    Task<bool> TryAdvanceTripCurrentDepartureAsync(
        Guid bookingId,
        DateTimeOffset expectedDeparture,
        DateTimeOffset newDeparture,
        DateTimeOffset updatedAt,
        CancellationToken ct = default)
        => throw new NotSupportedException("Schedule-change projection CAS is not implemented by this repository.");

    Task<IReadOnlyList<BookingEntity>> GetConfirmedByTripAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct = default)
        => throw new NotSupportedException("Schedule-change booking lookup is not implemented by this repository.");

    Task<IReadOnlyList<BookingEntity>> GetCancellableByTripAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct = default)
        => throw new NotSupportedException("Trip-cancellation booking lookup is not implemented by this repository.");

    Task<bool> HasOutboxEventAsync(
        string eventType,
        Guid eventId,
        CancellationToken ct = default)
        => throw new NotSupportedException("Outbox idempotency lookup is not implemented by this repository.");

    Task<TripEditImpactDto> GetTripEditImpactAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct = default)
        => throw new NotSupportedException("Trip-edit impact is not implemented by this repository.");

    Task<VehicleSubstitutionImpactDto> GetVehicleSubstitutionImpactAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct = default)
        => throw new NotSupportedException("Vehicle-substitution impact is not implemented by this repository.");

    Task<IReadOnlyList<BookingEntity>> GetVehicleSubstitutionBookingsForUpdateAsync(
        Guid oldTripId,
        Guid operatorId,
        IReadOnlyCollection<Guid> bookingIds,
        CancellationToken ct = default)
        => throw new NotSupportedException("Vehicle-substitution booking application is not implemented by this repository.");

    Task<int> GetPendingPassengerCountAsync(
        Guid tripId,
        Guid stopId,
        Guid operatorId,
        CancellationToken ct = default)
        => throw new NotSupportedException("Pending-passenger count is not implemented by this repository.");

    Task<OperatorBookingDetailDto?> GetOperatorBookingDetailAsync(Guid bookingId, Guid operatorId, CancellationToken ct = default)
        => throw new NotSupportedException("Operator booking detail is not implemented by this repository.");

    Task<bool> BookingExistsAsync(Guid bookingId, CancellationToken ct = default)
        => throw new NotSupportedException("Booking existence lookup is not implemented by this repository.");

    Task<OperatorBookingListPage> ListOperatorBookingsAsync(
        OperatorBookingListCriteria criteria,
        CancellationToken ct = default)
        => throw new NotSupportedException("Operator booking listing is not implemented by this repository.");

    /// <summary>
    /// Finds a booking by its booking code string (unique index).
    /// </summary>
    Task<BookingEntity?> FindByBookingCodeAsync(string bookingCode, CancellationToken ct = default);

    /// <summary>
    /// Finds a booking containing the ticket code and eagerly loads passengers + tickets.
    /// </summary>
    Task<BookingEntity?> FindByTicketCodeWithPassengersAsync(string ticketCode, CancellationToken ct = default);

    /// <summary>
    /// Finds a booking by id using the aggregate-specific seam.
    /// </summary>
    Task<BookingEntity?> FindByIdAsync(Guid bookingId, CancellationToken ct = default);

    Task<int> RelinkActiveStationReferencesAsync(
        IReadOnlyCollection<Guid> sourceStationIds,
        Guid canonicalStationId,
        DateTimeOffset updatedAt,
        CancellationToken ct = default)
        => throw new NotSupportedException("Active Booking Station relinking is not implemented by this repository.");

    /// <summary>
    /// Returns a booking with Passengers and Tickets eagerly loaded.
    /// Used for saga compensation checks and cancellation.
    /// </summary>
    Task<BookingEntity?> FindByIdWithPassengersAsync(Guid bookingId, CancellationToken ct = default);

    /// <summary>
    /// Locks one Booking aggregate for pending-action resolution and station-edit transactions.
    /// Pending-action callers lock the action first so concurrent resolution paths use one order.
    /// </summary>
    Task<BookingEntity?> FindByIdForUpdateAsync(Guid bookingId, CancellationToken ct = default)
        => throw new NotSupportedException("Booking pending-action lock lookup is not implemented by this repository.");

    Task<IReadOnlyList<BookingEntity>> GetNoShowCandidatesAsync(CancellationToken ct = default)
        => throw new NotSupportedException("No-show candidates are not implemented by this repository.");

    Task<BookingEntity?> FindConfirmedWithPassengersForUpdateAsync(
        Guid bookingId,
        CancellationToken ct = default)
        => throw new NotSupportedException("No-show booking lock lookup is not implemented by this repository.");

    /// <summary>
    /// Returns the data needed to replay the Trip seat-lock seam for a payment event.
    /// Null means the booking is no longer PENDING_PAYMENT and the event is an idempotent no-op.
    /// </summary>
    Task<BookingPaymentTransitionSnapshot?> GetPendingPaymentTransitionSnapshotAsync(
        Guid bookingId,
        CancellationToken ct = default);

    /// <summary>
    /// Status-guarded PENDING_PAYMENT -> CONFIRMED transition.
    /// Returns true only when this call changed the row.
    /// </summary>
    Task<bool> TryConfirmPendingPaymentAsync(
        Guid bookingId,
        DateTimeOffset confirmedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Status-guarded PENDING_PAYMENT -> EXPIRED transition.
    /// Returns true only when this call changed the row.
    /// </summary>
    Task<bool> TryExpirePendingPaymentAsync(
        Guid bookingId,
        DateTimeOffset expiredAt,
        CancellationToken ct = default);

    /// <summary>
    /// Status-guarded CONFIRMED/PENDING_PAYMENT -> CANCELLED transition.
    /// Returns true only when this call changed the row.
    /// </summary>
    Task<bool> TryCancelAsync(
        Guid bookingId,
        BookingCancellationReason reason,
        DateTimeOffset cancelledAt,
        bool refundOverride,
        CancellationToken ct = default);

    /// <summary>
    /// Status-guarded CANCELLED -> REFUNDED transition.
    /// Returns true only when this call changed the row.
    /// </summary>
    Task<bool> TryMarkCancelledRefundedAsync(
        Guid bookingId,
        DateTimeOffset refundedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically changes only CONFIRMED/PARTIAL_NO_SHOW bookings for the trip to COMPLETED.
    /// Returns the ids actually changed by this delivery so callers can append one history row
    /// per successful transition without duplicating history on replay.
    /// </summary>
    Task<IReadOnlyList<Guid>> TryCompleteEligibleByTripIdAsync(
        Guid tripId,
        DateTimeOffset completedAt,
        CancellationToken ct = default)
        => throw new NotSupportedException("Trip completion is not implemented by this repository.");

    Task<bool> HasConfirmedBookingAsync(Guid passengerUserId, CancellationToken ct = default);
}
