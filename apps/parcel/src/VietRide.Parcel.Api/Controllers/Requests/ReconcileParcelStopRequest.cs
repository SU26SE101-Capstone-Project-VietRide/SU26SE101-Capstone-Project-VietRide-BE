namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record ReconcileParcelStopRequest(
    IReadOnlyCollection<Guid>? ScannedParcelIds,
    IReadOnlyCollection<Guid>? ManualExceptionParcelIds,
    string? DepartureOverrideReason,
    Guid? SupervisorApprovalUserId);
