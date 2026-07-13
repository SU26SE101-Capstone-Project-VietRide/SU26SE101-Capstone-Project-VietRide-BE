using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public sealed class ShuttleDispatchAlert : BaseEntity<Guid>
{
    public Guid MainTripId { get; private set; }
    public Guid OperatorId { get; private set; }
    public string AlertType { get; private set; } = string.Empty;

    private ShuttleDispatchAlert() { }

    public static ShuttleDispatchAlert Create(
        Guid mainTripId,
        Guid operatorId,
        ShuttleDispatchAlertType alertType)
    {
        if (mainTripId == Guid.Empty || operatorId == Guid.Empty)
        {
            throw new ArgumentException("Main trip and operator are required.");
        }

        return new ShuttleDispatchAlert
        {
            Id = Guid.NewGuid(),
            MainTripId = mainTripId,
            OperatorId = operatorId,
            AlertType = alertType.ToString(),
        };
    }
}
