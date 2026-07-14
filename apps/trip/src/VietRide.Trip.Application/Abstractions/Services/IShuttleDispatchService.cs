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
}

public sealed record CreateShuttleTripInput(
    Guid OperatorId,
    Guid MainTripId,
    Guid DriverUserId,
    Guid VehicleId,
    DateTimeOffset ScheduledDepartureTime,
    DateTimeOffset ScheduledEndTime,
    IReadOnlyList<Guid> OrderedBookingIds,
    string? Notes);

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
    int DistanceToStationMeters,
    DateTimeOffset RequestedAt);

public sealed record ShuttleTrackingContext(
    Guid ShuttleTripId,
    Guid MainTripId,
    Guid OperatorId,
    Guid DriverUserId,
    bool Allowed,
    string? Scope,
    IReadOnlyList<ShuttleTrackingStop> Stops);

public sealed record ShuttleTrackingStop(
    int PickupOrder,
    Guid? BookingId,
    decimal Latitude,
    decimal Longitude,
    string Status,
    bool IsStation);
