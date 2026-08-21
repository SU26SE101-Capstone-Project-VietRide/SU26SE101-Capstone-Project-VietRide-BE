using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;
using VietRide.Parcel.Application.Services;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Parcels.AssistantActions;

public sealed class GetAssistantParcelActionStateQueryHandler
    : IRequestHandler<GetAssistantParcelActionStateQuery, AssistantParcelActionResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly ITripServiceClient _trips;
    private readonly IParcelReliabilityReadModelService _screenModels;

    public GetAssistantParcelActionStateQueryHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        ITripServiceClient trips,
        IParcelReliabilityReadModelService screenModels)
    {
        _parcels = parcels;
        _reliability = reliability;
        _trips = trips;
        _screenModels = screenModels;
    }

    public async Task<AssistantParcelActionResponse> Handle(
        GetAssistantParcelActionStateQuery request,
        CancellationToken cancellationToken)
    {
        // Mutation handlers may use ExecuteUpdate for atomic status transitions. A tracked
        // Parcel loaded earlier in the same HTTP scope would otherwise expose its stale status
        // in the screen-ready response returned immediately after the mutation.
        var parcel = await _parcels.QueryNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == request.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        if (parcel.OperatorId != request.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");
        var authorization = await _trips.AuthorizeAssistantForTripAsync(
            parcel.TripId,
            request.ActorUserId,
            request.OperatorId,
            cancellationToken);
        if (authorization.Kind != TripCrewAuthorizationOutcomeKind.Authorized)
            throw new ForbiddenException("FORBIDDEN", "Caller is not assigned to this parcel trip.");

        var screens = await _screenModels.BuildAsync([parcel], request.ActorUserId, false, cancellationToken);
        var screen = screens[parcel.Id];
        AssistantCreatedCustodyEventResponse? createdEvent = null;
        if (request.IncludeLatestCustodyEvent)
        {
            var latest = (await _reliability.ListCustodyEventsPageAsync(parcel.Id, null, 1, cancellationToken))
                .FirstOrDefault();
            if (latest is not null)
            {
                createdEvent = new AssistantCreatedCustodyEventResponse(
                    latest.Id,
                    latest.EventType.ToString(),
                    latest.ActualLocationType?.ToString(),
                    latest.ActualLocationId,
                    latest.LocationSnapshot,
                    latest.OccurredAt,
                    latest.Sequence);
            }
        }

        return new AssistantParcelActionResponse(
            new AssistantParcelActionStateResponse(
                parcel.Id,
                parcel.ParcelCode,
                parcel.Status.ToString(),
                screen.DropoffLocation,
                new AssistantParcelPaymentStateResponse(
                    parcel.DepositRequiredVnd.Amount,
                    parcel.DepositPaidVnd.Amount,
                    parcel.BalanceRequiredVnd.Amount,
                    parcel.BalancePaidVnd.Amount,
                    parcel.FinalPaymentDeadline,
                    parcel.DepositPaidVnd.Amount >= parcel.DepositRequiredVnd.Amount
                        && parcel.BalancePaidVnd.Amount >= parcel.BalanceRequiredVnd.Amount),
                new AssistantParcelIdentityHintsResponse(
                    parcel.PhotoUrl,
                    parcel.Description,
                    parcel.EstimatedWeightKg,
                    parcel.ActualWeightKg,
                    parcel.EstimatedLengthCm,
                    parcel.EstimatedWidthCm,
                    parcel.EstimatedHeightCm,
                    parcel.ActualLengthCm,
                    parcel.ActualWidthCm,
                    parcel.ActualHeightCm)),
            screen.Reliability.CurrentCustody,
            screen.Reliability.ActiveIncident,
            createdEvent,
            ParcelReliabilityActionResolver.Assistant(parcel, screen.Reliability.ActiveIncident is not null),
            request.Warning);
    }
}
