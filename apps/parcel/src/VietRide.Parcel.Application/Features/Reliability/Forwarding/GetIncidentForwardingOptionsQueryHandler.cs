using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;
using VietRide.Parcel.Application.Services;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Forwarding;

public sealed class GetIncidentForwardingOptionsQueryHandler
    : IRequestHandler<GetIncidentForwardingOptionsQuery, IReadOnlyList<IncidentForwardingOptionResponse>>
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelRepository _parcels;
    private readonly ITripServiceClient _trips;
    private readonly IClock _clock;

    public GetIncidentForwardingOptionsQueryHandler(
        IParcelReliabilityRepository reliability,
        IParcelRepository parcels,
        ITripServiceClient trips,
        IClock clock)
    {
        _reliability = reliability;
        _parcels = parcels;
        _trips = trips;
        _clock = clock;
    }

    public async Task<IReadOnlyList<IncidentForwardingOptionResponse>> Handle(
        GetIncidentForwardingOptionsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Limit is < 1 or > 50)
            throw new CodedValidationException("VALIDATION_ERROR", "limit must be between 1 and 50.");
        var incident = await _reliability.GetIncidentAsync(request.IncidentId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_INCIDENT_NOT_FOUND", "Incident was not found.");
        if (incident.OperatorId != request.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Incident does not belong to this operator.");
        if (incident.Status != ParcelIncidentStatus.FOUND)
            throw new CodedConflictException(
                "PARCEL_INCIDENT_INVALID_STATUS",
                "Forwarding options are available only after the parcel is found.");
        var parcel = await _parcels.GetByIdAsync(incident.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        var current = await _reliability.GetCurrentCustodyAsync(parcel.Id, cancellationToken);
        if (current?.LastLocationId is null || current.LastLocationType is null)
            throw new CodedConflictException(
                "PARCEL_CUSTODY_LOCATION_REQUIRED",
                "A confirmed current parcel location is required before forwarding.");

        var originalTripOutcome = await _trips.GetTripSummariesAsync([parcel.TripId], cancellationToken);
        if (originalTripOutcome.Kind != TripSummaryBatchOutcomeKind.Success
            || originalTripOutcome.Summaries.FirstOrDefault() is not { } originalTrip)
            throw new ParcelDependencyUnavailableException(
                "UPSTREAM_UNAVAILABLE",
                "Trip service is unavailable for forwarding option search.");
        var targetType = parcel.DropoffStopId.HasValue ? "ROUTE_STOP" : "DESTINATION_STATION";
        var targetId = parcel.DropoffStopId ?? originalTrip.Route.DestinationStationId;
        if (targetId == Guid.Empty)
            throw new CodedConflictException(
                "PARCEL_CUSTODY_LOCATION_REQUIRED",
                "The expected dropoff location is unavailable.");
        var options = await _trips.GetForwardingOptionsAsync(
            request.OperatorId,
            parcel.TripId,
            current.LastLocationType.Value.ToString(),
            current.LastLocationId.Value,
            targetType,
            targetId,
            parcel.ActualWeightKg ?? parcel.EstimatedWeightKg,
            parcel.ActualVolumeM3 ?? parcel.EstimatedVolumeM3,
            _clock.UtcNow,
            request.Limit,
            cancellationToken);
        if (!options.IsSuccess)
            throw new ParcelDependencyUnavailableException(
                "UPSTREAM_UNAVAILABLE",
                options.ErrorMessage ?? "Trip service is unavailable for forwarding option search.");

        return options.Options.Select(option =>
        {
            var trip = ParcelReliabilityReadModelService.MapTrip(option.Trip);
            return new IncidentForwardingOptionResponse(
                trip,
                trip.Route,
                trip.Vehicle,
                new ReliabilityLocationResponse(
                    option.PickupLocationType,
                    option.PickupLocationId,
                    option.PickupLocationName,
                    Eta: option.PickupAt),
                new ReliabilityLocationResponse(
                    option.TargetDropoffType,
                    option.TargetDropoffId,
                    option.TargetDropoffName,
                    Eta: option.Eta),
                trip.DepartureAt ?? option.PickupAt,
                option.Eta,
                option.CanReserve,
                option.UnavailableReason);
        }).ToArray();
    }
}
