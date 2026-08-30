using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed record ConfirmParcelFoundOnVehicleCommand(
    Guid ParcelId,
    Guid IncidentId,
    Guid OperatorId,
    Guid AssistantUserId,
    string ParcelCode,
    IReadOnlyCollection<string>? EvidenceReferences,
    string? Note,
    Guid IdempotencyKey) : IRequest<ConfirmParcelFoundOnVehicleResult>;

public sealed record ConfirmParcelFoundOnVehicleResult(
    Guid IncidentId,
    Guid CustodyEventId);
