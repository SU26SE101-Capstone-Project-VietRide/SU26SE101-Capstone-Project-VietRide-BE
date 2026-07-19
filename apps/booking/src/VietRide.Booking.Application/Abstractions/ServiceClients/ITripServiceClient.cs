namespace VietRide.Booking.Application.Abstractions.ServiceClients;

using VietRide.Booking.Application.Exceptions;

// ---------------------------------------------------------------------------
// Result / DTO records for the Trip seam (FROZEN — BSOT §13 row 1.8.0).
// Shapes match VietRide_API_Contract_v1.md lines 1065-1179 verbatim.
// ---------------------------------------------------------------------------

/// <summary>
/// Snapshot of a trip returned by GET /internal/v1/trips/{tripId}.
/// Raw DTO (no ApiResponse envelope — §1.6.2 internal-endpoint convention).
/// </summary>
public sealed record TripSnapshot(
    Guid TripId,
    Guid OperatorId,
    Guid RouteId,
    Guid VehicleId,
    string Status,
    DateTimeOffset DepartureDateTime,
    DateTimeOffset EstimatedArrivalTime,
    long BaseFare,
    TripStationSnapshot OriginStation,
    TripStationSnapshot DestinationStation,
    IReadOnlyList<TripStopSnapshot> Stops,
    TripSeatSummary SeatSummary,
    Guid? ReturnRouteId = null,
    Guid? DriverUserId = null,
    Guid? AssistantUserId = null,
    DateTimeOffset? DestinationArrivedAt = null,
    DateTimeOffset? ActualDepartureTime = null);

/// <summary>Station snapshot embedded in <see cref="TripSnapshot"/>.</summary>
public sealed record TripStationSnapshot(
    Guid Id,
    string Name,
    bool SupportsShuttle = false,
    decimal? Latitude = null,
    decimal? Longitude = null,
    bool IsActive = true);

/// <summary>Stop snapshot embedded in <see cref="TripSnapshot"/>.</summary>
public sealed record TripStopSnapshot(
    Guid StopId,
    int OrderIndex,
    bool AllowPickup,
    bool AllowDropoff,
    DateTimeOffset EstimatedArrivalTime,
    double DistanceFromOriginKm,
    long? FareFromThisStop,
    bool IsActive = true,
    string? Status = null,
    DateTimeOffset? ActualArrivalTime = null);

/// <summary>Seat availability summary embedded in <see cref="TripSnapshot"/>.</summary>
public sealed record TripSeatSummary(int TotalSeats, int AvailableSeats);

// ---------------------------------------------------------------------------
// Lock-seats result — discriminated union
// ---------------------------------------------------------------------------

/// <summary>
/// Successful result of POST /internal/v1/trips/{tripId}/lock-seats.
/// </summary>
public sealed record SeatLockResult(
    Guid SeatLockToken,
    IReadOnlyList<string> LockedSeats,
    DateTimeOffset ExpiresAt);

/// <summary>One leg returned by the atomic round-trip lock seam.</summary>
public sealed record RoundTripSeatLockResult(
    Guid TripId,
    Guid SeatLockToken,
    IReadOnlyList<string> LockedSeats,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Discriminated-union result of <see cref="ITripServiceClient.LockSeatsAsync"/>.
/// </summary>
public abstract record LockSeatsOutcome
{
    private LockSeatsOutcome() { }

    /// <summary>Lock succeeded; all seats are now HELD.</summary>
    public sealed record Success(SeatLockResult Data) : LockSeatsOutcome;

    /// <summary>
    /// 409 BOOKING_SEAT_UNAVAILABLE — at least one requested seat is not AVAILABLE.
    /// <paramref name="UnavailableSeats"/> lists the offending seat numbers.
    /// </summary>
    public sealed record SeatUnavailable(IReadOnlyList<string> UnavailableSeats) : LockSeatsOutcome;

    /// <summary>409 BOOKING_TRIP_NOT_BOOKABLE — trip status ≠ SCHEDULED.</summary>
    public sealed record TripNotBookable(string Message) : LockSeatsOutcome;

    /// <summary>404 TRIP_NOT_FOUND.</summary>
    public sealed record TripNotFound() : LockSeatsOutcome;

    /// <summary>Unexpected HTTP / transport error.</summary>
    public sealed record TransportError(string Message) : LockSeatsOutcome;
}

/// <summary>
/// Discriminated-union result of <see cref="ITripServiceClient.LockRoundTripSeatsAsync"/>.
/// Trip owns the real Redis Lua script; Booking must call this single seam for round-trip
/// checkout so both directions are held atomically, with no caller-visible half-lock window.
/// </summary>
public abstract record LockRoundTripSeatsOutcome
{
    private LockRoundTripSeatsOutcome() { }

    /// <summary>Both outbound and return leg locks succeeded atomically.</summary>
    public sealed record Success(
        RoundTripSeatLockResult Outbound,
        RoundTripSeatLockResult Return) : LockRoundTripSeatsOutcome;

    /// <summary>409 BOOKING_SEAT_UNAVAILABLE — no seats were retained.</summary>
    public sealed record SeatUnavailable(IReadOnlyList<string> UnavailableSeats) : LockRoundTripSeatsOutcome;

    /// <summary>409 BOOKING_TRIP_NOT_BOOKABLE — at least one trip status ≠ SCHEDULED.</summary>
    public sealed record TripNotBookable(string Message) : LockRoundTripSeatsOutcome;

    /// <summary>404 TRIP_NOT_FOUND.</summary>
    public sealed record TripNotFound(Guid TripId) : LockRoundTripSeatsOutcome;

    /// <summary>Unexpected HTTP / transport error.</summary>
    public sealed record TransportError(string Message) : LockRoundTripSeatsOutcome;
}

// ---------------------------------------------------------------------------
// ITripServiceClient
// ---------------------------------------------------------------------------

/// <summary>
/// Application-facing seam for the Trip inter-service HTTP client.
/// Covers the four endpoints in the Trip-Booking seat-lock saga
/// (BSOT §13 row 1.8.0, API Contract lines 1065-1179).
/// Location: Application/Abstractions/ServiceClients/ per BSOT §3.5 line 935.
/// </summary>
public interface ITripServiceClient
{
    /// <summary>
    /// GET /internal/v1/trips/{tripId} — returns a trip snapshot for fare
    /// calculation and pickup/dropoff validation. Returns <c>null</c> if 404.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="HttpRequestException"/> on transport failures (network error,
    /// 5xx responses, etc.). Callers in the CreateBooking handler should propagate this
    /// as a transient error rather than masking it.
    /// </remarks>
    Task<TripSnapshot?> GetTripSnapshotAsync(
        Guid tripId,
        CancellationToken cancellationToken = default);

    async Task<TripSnapshot> GetOperationalTripSnapshotAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
        => await GetTripSnapshotAsync(tripId, cancellationToken)
            ?? throw new BookingUpstreamUnavailableException("Trip operational snapshot is unavailable.");

    /// <summary>
    /// GET /internal/v1/trips/{tripId}?pricingAt=... — returns a trip snapshot whose
    /// stop fares are resolved for the supplied pricing instant. New Booking creation
    /// captures this value once at handler start and reuses it for every leg.
    /// </summary>
    Task<TripSnapshot?> GetTripSnapshotAsync(
        Guid tripId,
        DateTimeOffset pricingAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// POST /internal/v1/trips/{tripId}/lock-seats — all-or-nothing seat hold.
    /// Idempotent: same Idempotency-Key returns the same <see cref="SeatLockResult"/>.
    /// Returns a <see cref="LockSeatsOutcome"/> discriminated union.
    /// </summary>
    Task<LockSeatsOutcome> LockSeatsAsync(
        Guid tripId,
        IReadOnlyList<string> seatNumbers,
        Guid holdOwnerId,
        string idempotencyKey,
        int? ttlSeconds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /internal/v1/trips/round-trip/lock-seats — atomically holds seats for both
    /// directions in one Trip-owned Redis Lua script. If either leg cannot be held, no
    /// seats are retained and Booking receives a non-success outcome.
    /// </summary>
    Task<LockRoundTripSeatsOutcome> LockRoundTripSeatsAsync(
        Guid outboundTripId,
        IReadOnlyList<string> outboundSeatNumbers,
        Guid returnTripId,
        IReadOnlyList<string> returnSeatNumbers,
        Guid holdOwnerId,
        string idempotencyKey,
        int? ttlSeconds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /internal/v1/trips/{tripId}/book-seats — flips HELD → BOOKED after
    /// payment success. Idempotent (204 no-op if already booked).
    /// Throws <see cref="HttpRequestException"/> on transport failure;
    /// returns <c>false</c> if the lock token has expired (409 BOOKING_SEAT_UNAVAILABLE).
    /// </summary>
    Task<bool> BookSeatsAsync(
        Guid tripId,
        Guid seatLockToken,
        Guid bookingId,
        IReadOnlyList<PassengerSeatAssignment> passengerSeatAssignments,
        CancellationToken cancellationToken = default);

    Task<bool> BookRoundTripSeatsAsync(
        RoundTripBookSeatsLeg outbound,
        RoundTripBookSeatsLeg @return,
        CancellationToken cancellationToken = default)
        => Task.FromException<bool>(new NotSupportedException("Round-trip seat confirmation is not supported."));

    /// <summary>
    /// POST /internal/v1/trips/{tripId}/release-seats — compensation (204 idempotent).
    /// Releasing an already-released or expired lock is a no-op.
    /// </summary>
    Task ReleaseSeatsAsync(
        Guid tripId,
        Guid seatLockToken,
        IReadOnlyList<string> seatNumbers,
        CancellationToken cancellationToken = default);
}

/// <summary>Seat assignment for a passenger in book-seats.</summary>
public sealed record PassengerSeatAssignment(Guid PassengerId, string SeatNumber);

public sealed record RoundTripBookSeatsLeg(
    Guid TripId,
    Guid SeatLockToken,
    Guid BookingId,
    IReadOnlyList<PassengerSeatAssignment> PassengerSeatAssignments);
