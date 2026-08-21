namespace VietRide.Parcel.Application.Features.Parcels.AssistantActions;

public sealed record AssistantCreatedCustodyEventResponse(
    Guid EventId,
    string EventType,
    string? ActualLocationType,
    Guid? ActualLocationId,
    string? LocationSnapshot,
    DateTimeOffset OccurredAt,
    int Sequence);
