namespace VietRide.Trip.Application.Abstractions.Services;

public static class ShuttleRoutePreviewStatuses
{
    public const string Safe = "SAFE";
    public const string LateRisk = "LATE_RISK";
    public const string Unknown = "UNKNOWN";
    public const string NotApplicable = "NOT_APPLICABLE";
}

public sealed record ShuttleRoutePreviewInput(
    Guid OperatorId,
    Guid MainTripId,
    string Direction,
    DateTimeOffset ScheduledDepartureTime,
    IReadOnlyList<Guid> OrderedBookingIds);

public sealed record ShuttleRoutePreviewResult(
    string Status,
    DateTimeOffset? EstimatedFinishAt,
    DateTimeOffset? HardCutoffAt,
    int? DelayMinutes,
    string? WarningCode,
    bool LateRiskBlocksCreate,
    string? Basis);

public interface IShuttleRoutePreviewService
{
    Task<ShuttleRoutePreviewResult> PreviewAsync(
        ShuttleRoutePreviewInput input,
        CancellationToken cancellationToken = default);
}
