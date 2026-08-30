using System.Text.Json;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public static class ParcelTripCompletionClearanceGuard
{
    public static async Task EnsureAsync(
        IParcelImpactClient parcels,
        Guid tripId,
        Guid operatorId,
        bool allowAcknowledgedIncidents,
        CancellationToken cancellationToken)
    {
        ParcelTripCompletionClearanceProjection clearance;
        try
        {
            clearance = await parcels.GetTripCompletionClearanceAsync(
                tripId,
                operatorId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TripUpstreamUnavailableException("Parcel Trip-completion clearance timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new TripUpstreamUnavailableException(
                "Parcel Trip-completion clearance is unavailable.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new TripUpstreamUnavailableException(
                "Parcel Trip-completion clearance returned malformed data.",
                exception);
        }

        if (clearance.Status == "CLEAR")
            return;
        if (clearance.Status == "ACKNOWLEDGED_INCIDENTS" && allowAcknowledgedIncidents)
            return;

        throw new CodedConflictException(
            "PARCEL_DESTINATION_RECONCILIATION_REQUIRED",
            clearance.Status == "ACKNOWLEDGED_INCIDENTS"
                ? "Only the assigned Driver can complete a Trip with unresolved destination Parcels."
                : "Destination Parcels must be unloaded or reconciled before completing the Trip.",
            [
                new ValidationError(
                    "unresolvedParcelIds",
                    string.Join(',', clearance.UnresolvedParcelIds)),
                new ValidationError(
                    "incidentIds",
                    string.Join(',', clearance.IncidentIds)),
                new ValidationError(
                    "requiredAction",
                    clearance.Status == "ACKNOWLEDGED_INCIDENTS"
                        ? "DRIVER_COMPLETE_ACKNOWLEDGED_INCIDENTS"
                        : "RECONCILE_DESTINATION_PARCELS"),
            ]);
    }
}
