using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public sealed class OperatorFareSurchargeSetting : BaseEntity<Guid>
{
    public Guid OperatorId => Id;
    public bool IsEnabled { get; private set; }

    private OperatorFareSurchargeSetting() { }

    public static OperatorFareSurchargeSetting Create(Guid operatorId, bool isEnabled)
    {
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Operator id cannot be empty.", nameof(operatorId));

        return new OperatorFareSurchargeSetting
        {
            Id = operatorId,
            IsEnabled = isEnabled,
        };
    }

    public void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;
}
