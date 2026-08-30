using MediatR;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed record ReconcileParcelStopCommand(
    Guid TripId,
    Guid StopId,
    Guid ActorUserId,
    Guid OperatorId,
    string? DepartureOverrideReason,
    Guid IdempotencyKey) : IRequest<ReconcileParcelStopResponse>;

public sealed record ReconcileParcelStopResponse(
    int ExpectedCount,
    int ScannedCount,
    int ManualExceptionCount,
    IReadOnlyList<ReconcileUnresolvedParcelResponse> UnresolvedParcels,
    bool CanDepart,
    bool RequiresSupervisorApproval,
    ParcelStopDepartureApprovalResponse? DepartureOverrideRequest)
{
    public IReadOnlyList<Guid> UnresolvedParcelIds
        => UnresolvedParcels.Select(parcel => parcel.ParcelId).ToArray();
}

public sealed record ReconcileUnresolvedParcelResponse(
    Guid ParcelId,
    string ParcelCode,
    string? PhotoUrl,
    ReliabilityLocationResponse ExpectedDropoff,
    ReliabilityCustodySummaryResponse? LastCustody,
    Guid? IncidentId,
    string? IncidentType,
    string Reason,
    string RecommendedAction);
