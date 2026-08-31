using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed record ReconcileParcelDestinationCommand(
    Guid TripId,
    Guid ActorUserId,
    Guid OperatorId,
    Guid IdempotencyKey) : IRequest<ReconcileParcelDestinationResponse>;

public sealed record ReconcileParcelDestinationResponse(
    int ExpectedCount,
    int ScannedCount,
    int ManualExceptionCount,
    IReadOnlyList<ReconcileUnresolvedParcelResponse> UnresolvedParcels,
    bool CanComplete,
    bool CanCompleteTrip,
    bool AllExpectedParcelsDelivered,
    bool RequiresDriverCompletion);
