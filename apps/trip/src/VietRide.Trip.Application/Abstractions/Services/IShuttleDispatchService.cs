using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Features.Shuttle;

namespace VietRide.Trip.Application.Abstractions.Services;

public interface IShuttleDispatchService
{
    Task<PagedResult<ShuttleRequestTripGroup>> GetPendingAsync(
        Guid operatorId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<PagedResult<OperatorShuttleTripListItemDto>> GetHistoryAsync(
        Guid operatorId,
        int page,
        int pageSize,
        DateOnly? from,
        DateOnly? to,
        IReadOnlyCollection<string>? statuses,
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

    Task<ShuttleDriverAssignmentPage> GetDriverAssignmentsAsync(
        Guid driverUserId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);

    Task<ShuttleDriverManifest> GetDriverManifestAsync(
        Guid shuttleTripId,
        Guid driverUserId,
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

public sealed record ShuttleRequestTripGroup(
    Guid MainTripId,
    string RouteName,
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
    int? RoadDistanceMeters,
    IReadOnlyList<ShuttlePassengerProfile> Passengers);

public sealed record ShuttlePassengerProfile(
    Guid? PassengerUserId,
    string? DisplayName,
    string? Phone,
    IReadOnlyList<Guid> TicketIds);

public sealed record OperatorShuttleTripListItemDto(
    Guid ShuttleTripId,
    Guid MainTripId,
    string Direction,
    string Status,
    DateTimeOffset ScheduledDepartureTime,
    DateTimeOffset ScheduledEndTime,
    DateTimeOffset? ActualDepartureTime,
    DateTimeOffset? CompletedAt,
    OperatorShuttleVehicleDto Vehicle,
    OperatorShuttleDriverDto Driver,
    int PassengerCount,
    int StopCount);

public sealed record OperatorShuttleVehicleDto(Guid Id, string LicensePlate);

public sealed record OperatorShuttleDriverDto(Guid Id, string? DisplayName, string? Phone);

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

public sealed record ShuttleDriverAssignmentPage(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ShuttleDriverAssignment> Items);

public sealed record ShuttleDriverAssignment(
    Guid ShuttleTripId,
    Guid MainTripId,
    string Direction,
    string Status,
    Guid VehicleId,
    string LicensePlate,
    DateTimeOffset ScheduledDepartureTime,
    DateTimeOffset ScheduledEndTime,
    int PassengerCount,
    int StopCount);

public sealed record ShuttleDriverManifest(
    Guid ShuttleTripId,
    Guid MainTripId,
    string Direction,
    string Status,
    Guid StationId,
    string StationName,
    decimal? StationLatitude,
    decimal? StationLongitude,
    DateTimeOffset ScheduledDepartureTime,
    DateTimeOffset ScheduledEndTime,
    IReadOnlyList<ShuttleDriverManifestStop> Stops);

public sealed record ShuttleDriverManifestStop(
    int PickupOrder,
    Guid? BookingId,
    IReadOnlyList<Guid> TicketIds,
    int PassengerCount,
    string PickupAddress,
    decimal PickupLatitude,
    decimal PickupLongitude,
    string Status,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? DeliveredAt,
    string? PassengerDisplayName,
    string? PassengerPhone);
