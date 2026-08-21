using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;
using VietRide.Parcel.Application.Services;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed class GetAssistantTripParcelsQueryHandler
    : IRequestHandler<GetAssistantTripParcelsQuery, AssistantTripParcelManifestResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IParcelReliabilityReadModelService _screenModels;

    public GetAssistantTripParcelsQueryHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient,
        IParcelReliabilityReadModelService screenModels)
    {
        _parcelRepository = parcelRepository;
        _tripClient = tripClient;
        _screenModels = screenModels;
    }

    public async Task<AssistantTripParcelManifestResponse> Handle(
        GetAssistantTripParcelsQuery query,
        CancellationToken cancellationToken)
    {
        var authorization = await _tripClient.AuthorizeAssistantForTripAsync(
            query.TripId,
            query.UserId,
            query.OperatorId,
            cancellationToken);

        switch (authorization.Kind)
        {
            case TripCrewAuthorizationOutcomeKind.Authorized:
                break;
            case TripCrewAuthorizationOutcomeKind.Denied:
            case TripCrewAuthorizationOutcomeKind.TripNotFound:
                throw new ForbiddenException("FORBIDDEN", "Caller is not authorized to view parcels for this trip.");
            default:
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    authorization.ErrorMessage ?? "Trip service unavailable.");
        }

        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "Invalid paging values.");
        if (query.Search?.Length > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "search must not exceed 100 characters.");
        ParcelStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse(query.Status, true, out ParcelStatus parsedStatus)
                || !Enum.IsDefined(parsedStatus))
                throw new CodedValidationException("VALIDATION_ERROR", "status is invalid.");
            status = parsedStatus;
        }

        var pagedResult = await _parcelRepository.ListByTripAndOperatorFilteredAsync(
            query.TripId,
            query.OperatorId,
            query.StopId,
            status,
            query.HasException,
            query.Search,
            query.Page,
            query.PageSize,
            cancellationToken);

        var screens = await _screenModels.BuildAsync(
            pagedResult.Items,
            query.UserId,
            includeClaim: false,
            cancellationToken);
        var trip = screens.Values.FirstOrDefault()?.Trip;
        if (trip is null)
        {
            var tripOutcome = await _tripClient.GetTripSummariesAsync([query.TripId], cancellationToken);
            if (tripOutcome.Kind != TripSummaryBatchOutcomeKind.Success
                || tripOutcome.Summaries.FirstOrDefault() is not { } tripSummary)
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    "Trip manifest context is temporarily unavailable.");
            trip = ParcelReliabilityReadModelService.MapTrip(tripSummary);
        }

        var currentStop = trip.Stops
            .Where(stop => string.Equals(stop.Status, "ARRIVED", StringComparison.OrdinalIgnoreCase)
                && !stop.ActualDepartureAt.HasValue)
            .OrderByDescending(stop => stop.OrderIndex)
            .FirstOrDefault();
        var counts = await _parcelRepository.GetAssistantManifestCountsAsync(
            query.TripId,
            query.OperatorId,
            currentStop?.StopId,
            cancellationToken);

        var items = pagedResult.Items.Select(parcel => new AssistantTripParcelResponse(
            parcel.Id,
            parcel.ParcelCode,
            parcel.Status.ToString(),
            parcel.RecipientName,
            parcel.RecipientPhone.ToString(),
            parcel.DropoffStopId,
            parcel.SizeCategory.ToString(),
            parcel.EstimatedSizeCategory.ToString(),
            parcel.ActualSizeCategory?.ToString(),
            parcel.EstimatedWeightKg,
            parcel.ActualWeightKg,
            parcel.BalanceRequiredVnd.Amount,
            parcel.BalancePaidVnd.Amount,
            parcel.FinalPaymentDeadline,
            parcel.Description,
            parcel.PhotoUrl,
            screens.GetValueOrDefault(parcel.Id)?.DropoffLocation,
            screens.GetValueOrDefault(parcel.Id)?.Reliability.CurrentCustody,
            screens.GetValueOrDefault(parcel.Id)?.Reliability.ActiveIncident,
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
                parcel.ActualHeightCm),
            ParcelReliabilityActionResolver.Assistant(
                parcel,
                screens.GetValueOrDefault(parcel.Id)?.Reliability.ActiveIncident is not null))).ToList();

        return new AssistantTripParcelManifestResponse(
            new AssistantTripManifestContextResponse(
                trip,
                currentStop is null
                    ? null
                    : new AssistantOperationalLocationResponse(
                        new ReliabilityLocationResponse(
                            "ROUTE_STOP",
                            currentStop.StopId,
                            currentStop.Name,
                            currentStop.OrderIndex,
                            currentStop.EstimatedArrivalAt),
                        currentStop.Status,
                        currentStop.ActualArrivalAt,
                        currentStop.ActualDepartureAt),
                trip.Stops),
            new AssistantTripManifestSummaryResponse(
                counts.Total,
                counts.CheckedIn,
                counts.Loaded,
                counts.ExpectedAtCurrentStop,
                counts.Unloaded,
                counts.ExceptionCount,
                counts.UnresolvedCount),
            items,
            new AssistantTripManifestPaginationResponse(
                pagedResult.Page,
                pagedResult.PageSize,
                pagedResult.TotalItems,
                pagedResult.TotalPages,
                pagedResult.HasNextPage,
                pagedResult.HasPreviousPage));
    }
}
