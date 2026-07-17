using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Domain.Entities;

public sealed class BookingStationRedirect : IAuditable
{
    private BookingStationRedirect() { }

    public Guid DuplicateStationId { get; private set; }
    public Guid CanonicalStationId { get; private set; }
    public Guid SourceEventId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static BookingStationRedirect Create(
        Guid duplicateStationId,
        Guid canonicalStationId,
        Guid sourceEventId,
        DateTimeOffset occurredAt)
    {
        if (duplicateStationId == Guid.Empty)
            throw new ArgumentException("Duplicate Station id is required.", nameof(duplicateStationId));
        if (canonicalStationId == Guid.Empty)
            throw new ArgumentException("Canonical Station id is required.", nameof(canonicalStationId));
        if (sourceEventId == Guid.Empty)
            throw new ArgumentException("Source event id is required.", nameof(sourceEventId));
        if (duplicateStationId == canonicalStationId)
            throw new ArgumentException("A Station redirect cannot target itself.", nameof(canonicalStationId));

        return new BookingStationRedirect
        {
            DuplicateStationId = duplicateStationId,
            CanonicalStationId = canonicalStationId,
            SourceEventId = sourceEventId,
            OccurredAt = occurredAt,
        };
    }

    public void FlattenTo(Guid canonicalStationId)
    {
        if (canonicalStationId == Guid.Empty || canonicalStationId == DuplicateStationId)
            throw new ArgumentException("The flattened canonical Station id is invalid.", nameof(canonicalStationId));

        CanonicalStationId = canonicalStationId;
    }
}
