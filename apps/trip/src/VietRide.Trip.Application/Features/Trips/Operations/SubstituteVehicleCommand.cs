using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.Trips.Operations;

[SkipTransaction]
public sealed record SubstituteVehicleCommand(
    Guid TripId,
    Guid OperatorId,
    Guid ActorUserId,
    Guid ReplacementVehicleId,
    DateTimeOffset EstimatedRecoveryDepartureAt,
    string Reason,
    Guid? IncidentId,
    bool NotifyPassengers,
    Guid? ReplacementDriverId,
    Guid? ReplacementAssistantId,
    bool ReplacementCrewSpecified,
    bool AcknowledgeInsufficientSeats = false,
    string? PreviewToken = null,
    IReadOnlyList<SubstituteVehicleSeatAssignment>? SeatAssignments = null) : IRequest<SubstituteVehicleResponse>;
