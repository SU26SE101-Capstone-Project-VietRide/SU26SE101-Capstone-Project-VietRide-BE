using System.Net.Http.Json;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Infrastructure.Http;

public sealed class ParcelImpactClient(HttpClient httpClient) : IParcelImpactClient
{
    public async Task<ParcelTripCompletionClearanceProjection> GetTripCompletionClearanceAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        if (tripId == Guid.Empty || operatorId == Guid.Empty)
            throw new ArgumentException("Trip and operator ids must be non-empty.");

        var response = await httpClient.GetFromJsonAsync<ParcelTripCompletionClearanceProjection>(
            $"/internal/v1/parcels/trips/{tripId:D}/completion-clearance?operatorId={operatorId:D}",
            cancellationToken);
        if (response is null
            || response.TripId != tripId
            || response.OperatorId != operatorId
            || response.Status is not ("CLEAR" or "ACKNOWLEDGED_INCIDENTS" or "BLOCKED_RECONCILIATION")
            || response.UnresolvedParcelIds is null
            || response.IncidentIds is null
            || response.UnresolvedParcelIds.Any(id => id == Guid.Empty)
            || response.UnresolvedParcelIds.Distinct().Count() != response.UnresolvedParcelIds.Count
            || response.IncidentIds.Any(id => id == Guid.Empty)
            || response.IncidentIds.Distinct().Count() != response.IncidentIds.Count
            || (response.Status == "CLEAR"
                && (response.UnresolvedParcelIds.Count != 0 || response.IncidentIds.Count != 0))
            || (response.Status == "ACKNOWLEDGED_INCIDENTS"
                && (response.UnresolvedParcelIds.Count == 0
                    || response.IncidentIds.Count != response.UnresolvedParcelIds.Count))
            || (response.Status == "BLOCKED_RECONCILIATION"
                && response.UnresolvedParcelIds.Count == 0))
            throw new HttpRequestException("Parcel Trip-completion clearance returned invalid data.");

        return response;
    }

    public async Task<ParcelStopDepartureClearanceProjection> GetStopDepartureClearanceAsync(
        Guid tripId,
        Guid stopId,
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        if (tripId == Guid.Empty || stopId == Guid.Empty || operatorId == Guid.Empty)
            throw new ArgumentException("Trip, stop and operator ids must be non-empty.");

        var response = await httpClient.GetFromJsonAsync<ParcelStopDepartureClearanceProjection>(
            $"/internal/v1/parcels/trips/{tripId:D}/stops/{stopId:D}/departure-clearance?operatorId={operatorId:D}",
            cancellationToken);
        if (response is null
            || response.TripId != tripId
            || response.StopId != stopId
            || response.OperatorId != operatorId
            || response.Status is not ("CLEAR" or "APPROVED_OVERRIDE" or "BLOCKED_PENDING_APPROVAL")
            || response.UnresolvedParcelIds is null
            || response.UnresolvedParcelIds.Any(id => id == Guid.Empty)
            || response.UnresolvedParcelIds.Distinct().Count() != response.UnresolvedParcelIds.Count
            || (response.Status == "CLEAR" && response.UnresolvedParcelIds.Count != 0)
            || (response.Status == "APPROVED_OVERRIDE"
                && (response.UnresolvedParcelIds.Count == 0
                    || !response.ApprovalRequestId.HasValue
                    || !response.ApprovedByUserId.HasValue
                    || !response.ApprovedAt.HasValue))
            || (response.Status == "BLOCKED_PENDING_APPROVAL"
                && response.UnresolvedParcelIds.Count == 0))
            throw new HttpRequestException("Parcel stop-departure clearance returned invalid data.");

        return response;
    }

    public async Task<TripParcelCancellationImpactProjection> GetTripCancellationImpactAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        if (tripId == Guid.Empty || operatorId == Guid.Empty)
            throw new ArgumentException("Trip and operator ids must be non-empty.");

        var response = await httpClient.GetFromJsonAsync<TripParcelCancellationImpactProjection>(
            $"/internal/v1/parcels/trips/{tripId:D}/cancel-impact?operatorId={operatorId:D}",
            cancellationToken);
        if (response is null
            || response.TripId != tripId
            || response.AffectedParcels is null
            || response.AffectedParcels.Any(parcel =>
                parcel.ParcelId == Guid.Empty || parcel.RefundAmount < 0)
            || response.AffectedParcels.Select(parcel => parcel.ParcelId).Distinct().Count()
                != response.AffectedParcels.Count)
        {
            throw new HttpRequestException("Parcel Trip-cancellation impact returned invalid data.");
        }

        return response;
    }
}
