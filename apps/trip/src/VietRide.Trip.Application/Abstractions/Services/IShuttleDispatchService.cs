namespace VietRide.Trip.Application.Abstractions.Services;

public interface IShuttleDispatchService
{
    Task<ShuttleRequestPage> GetPendingAsync(
        Guid operatorId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<CreateShuttleTripResult> CreateAsync(
        CreateShuttleTripInput input,
        CancellationToken cancellationToken);

    Task<ShuttleTrackingContext> GetTrackingContextAsync(
        Guid shuttleTripId,
        Guid userId,
        string role,
        Guid? operatorId,
        CancellationToken cancellationToken);

    Task<ShuttlePickupResult> MarkPickupAsync(
        Guid shuttleTripId,
        int pickupOrder,
        Guid driverUserId,
        CancellationToken cancellationToken);

    Task<ShuttleLifecycleResult> MarkDeliveredAsync(
        Guid shuttleTripId,
        int pickupOrder,
        Guid driverUserId,
        CancellationToken cancellationToken);

    Task<ShuttleLifecycleResult> MarkNoShowAsync(
        Guid shuttleTripId,
        int pickupOrder,
        Guid driverUserId,
        string reason,
        CancellationToken cancellationToken);

    Task<ShuttleLifecycleResult> StartAsync(
        Guid shuttleTripId,
        Guid driverUserId,
        CancellationToken cancellationToken);

    Task<ShuttleLifecycleResult> CompleteAsync(
        Guid shuttleTripId,
        Guid driverUserId,
        CancellationToken cancellationToken);

    Task<ShuttleLifecycleResult> CancelRequestAsync(
        Guid operatorId,
        Guid mainTripId,
        Guid bookingId,
        string direction,
        string reason,
        CancellationToken cancellationToken);

    Task<ShuttleLifecycleResult> CancelShuttleTripAsync(
        Guid operatorId,
        Guid shuttleTripId,
        string reason,
        CancellationToken cancellationToken);
}

public sealed record CreateShuttleTripInput(
    Guid OperatorId,
    Guid MainTripId,
    Guid DriverUserId,
    Guid VehicleId,
    DateTimeOffset ScheduledDepartureTime,
    DateTimeOffset ScheduledEndTime,
    IReadOnlyList<Guid> OrderedBookingIds,
    string? Notes,
    string Direction = "INBOUND_TO_STATION");

public sealed record CreateShuttleTripResult(
    Guid ShuttleTripId,
    Guid MainTripId,
    int AssignedPassengerCount,
    int RemainingPassengerCount);

public sealed record ShuttleRequestPage(
    IReadOnlyList<ShuttleRequestTripGroup> Items,
    int Page,
    int PageSize,
    int TotalItems);

public sealed record ShuttleRequestTripGroup(
    Guid MainTripId,
    string Direction,
    DateTimeOffset DepartureDateTime,
    DateTimeOffset HardCutoffAt,
    Guid StationId,
    string StationName,
    int PendingPassengerCount,
    IReadOnlyList<ShuttleBookingGroup> BookingGroups,
    IReadOnlyList<Guid> SuggestedBookingOrder);

public sealed record ShuttleBookingGroup(
    Guid BookingId,
    int PassengerCount,
    string PickupAddress,
    decimal PickupLat,
    decimal PickupLng,
    int? DistanceToStationMeters,
    DateTimeOffset RequestedAt,
    int? RoadDistanceMeters = null);

public sealed record ShuttleTrackingContext(
    Guid ShuttleTripId,
    Guid MainTripId,
    Guid OperatorId,
    Guid DriverUserId,
    bool Allowed,
    string? Scope,
    IReadOnlyList<ShuttleTrackingStop> Stops,
    ShuttleTrackingStation? Station = null,
    string Direction = "INBOUND_TO_STATION",
    string Status = "SCHEDULED");

public sealed record ShuttleTrackingStop(
    int PickupOrder,
    Guid? BookingId,
    decimal Latitude,
    decimal Longitude,
    string Status,
    bool IsStation,
    bool IsOwnPickup = false,
    string? ServiceAddress = null,
    int? ServiceOrder = null,
    int? RoadDistanceSnapshotMeters = null);
