using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Domain.Entities;

public sealed class OperatorWalletBackfillMarker : BaseEntity<Guid>
{
    private OperatorWalletBackfillMarker() { }

    public Guid OperatorId { get; private set; }
    public Guid EventId { get; private set; }

    public static OperatorWalletBackfillMarker Create(Guid operatorId, Guid eventId)
    {
        if (operatorId == Guid.Empty || eventId == Guid.Empty)
            throw new ArgumentException("Operator and event ids are required.");

        return new OperatorWalletBackfillMarker
        {
            Id = operatorId,
            OperatorId = operatorId,
            EventId = eventId,
        };
    }
}
