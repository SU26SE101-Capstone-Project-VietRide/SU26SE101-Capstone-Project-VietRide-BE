using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed record HandleTripDisruptedCommand(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid TripId,
    Guid OperatorId,
    DateTimeOffset TerminalAt,
    bool HasSubstitution,
    string? Reason) : IRequest<int>;
