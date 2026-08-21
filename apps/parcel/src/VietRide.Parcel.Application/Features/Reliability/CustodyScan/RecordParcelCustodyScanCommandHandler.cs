using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.CustodyScan;

public sealed class RecordParcelCustodyScanCommandHandler
    : IRequestHandler<RecordParcelCustodyScanCommand, ParcelCustodyScanResponse>
{
    private static readonly HashSet<ParcelCustodyEventType> AllowedScanEvents =
    [
        ParcelCustodyEventType.ACCEPTED,
        ParcelCustodyEventType.ARRIVED_AT_STOP,
        ParcelCustodyEventType.HANDOFF,
        ParcelCustodyEventType.RETURNED_TO_STATION,
    ];

    private readonly IParcelRepository _parcels;
    private readonly IParcelCustodyService _custody;
    private readonly ITripServiceClient _trips;

    public RecordParcelCustodyScanCommandHandler(
        IParcelRepository parcels,
        IParcelCustodyService custody,
        ITripServiceClient trips)
    {
        _parcels = parcels;
        _custody = custody;
        _trips = trips;
    }

    public async Task<ParcelCustodyScanResponse> Handle(
        RecordParcelCustodyScanCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcels.GetByIdAsync(command.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");
        if (!string.Equals(parcel.ParcelCode, command.ParcelCode?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new CodedConflictException(
                "SCAN_IDENTITY_MISMATCH",
                "The scanned QR code does not belong to this parcel.",
                [new ValidationError("requiredAction", "VERIFY_PARCEL_IDENTITY")]);
        if (!Enum.TryParse<ParcelCustodyEventType>(command.EventType, true, out var eventType)
            || !AllowedScanEvents.Contains(eventType))
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "eventType is not permitted for a direct custody scan.");
        if (!Enum.TryParse<ParcelCustodyLocationType>(command.ActualLocationType, true, out var locationType))
            throw new CodedValidationException("PARCEL_CUSTODY_LOCATION_REQUIRED", "Actual location type is invalid.");
        if (locationType != ParcelCustodyLocationType.VEHICLE && !command.ActualLocationId.HasValue)
            throw new CodedValidationException(
                "PARCEL_CUSTODY_LOCATION_REQUIRED",
                "A location id is required for this custody location type.");

        if (command.RequireAssignedCrew)
        {
            var authorization = await _trips.AuthorizeAssistantForTripAsync(
                parcel.TripId,
                command.ActorUserId,
                command.OperatorId,
                cancellationToken);
            if (authorization.Kind != TripCrewAuthorizationOutcomeKind.Authorized)
                throw new ForbiddenException("FORBIDDEN", "Only the assigned assistant can scan this parcel.");
        }

        var custodyEvent = await _custody.AppendAsync(
            parcel,
            eventType,
            locationType,
            command.ActualLocationId,
            command.LocationSnapshot,
            command.ActorUserId,
            command.ActorRole,
            "CUSTODY_SCAN",
            command.IdempotencyKey.ToString("D"),
            command.EvidenceReferences,
            command.Reason,
            cancellationToken);
        return new ParcelCustodyScanResponse(
            custodyEvent.Id,
            parcel.Id,
            custodyEvent.EventType.ToString(),
            custodyEvent.ActualLocationType?.ToString(),
            custodyEvent.ActualLocationId,
            custodyEvent.OccurredAt,
            custodyEvent.Sequence);
    }
}
