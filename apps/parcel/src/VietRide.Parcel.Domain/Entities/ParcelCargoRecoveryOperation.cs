using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelCargoRecoveryOperation : BaseEntity<Guid>
{
    public Guid ParcelId { get; private set; }
    public Guid OperatorId { get; private set; }
    public ParcelCargoRecoveryOperationType OperationType { get; private set; }
    public ParcelCargoRecoveryOperationStatus Status { get; private set; }
    public Guid SourceTripId { get; private set; }
    public Guid? TargetTripId { get; private set; }
    public string? TargetState { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Reason { get; private set; } = null!;
    public long RefundAmountVnd { get; private set; }
    public long RefundDueVnd { get; private set; }
    public ParcelStatus SourceStatus { get; private set; }
    public bool IsStatusOverride { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? FailureCode { get; private set; }

    private ParcelCargoRecoveryOperation()
    {
    }
}
