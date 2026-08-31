using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Services;

public sealed record ParcelTripCompletionClearanceDecision(
    string Status,
    IReadOnlyList<Guid> UnresolvedParcelIds,
    IReadOnlyList<Guid> IncidentIds)
{
    public bool CanCompleteTrip => Status is "CLEAR" or "ACKNOWLEDGED_INCIDENTS";
    public bool RequiresDriverCompletion => Status == "ACKNOWLEDGED_INCIDENTS";
}

public static class ParcelTripCompletionClearancePolicy
{
    public static ParcelTripCompletionClearanceDecision Evaluate(
        IReadOnlyCollection<VietRide.Parcel.Domain.Entities.Parcel> manifest,
        IReadOnlyCollection<ParcelIncident> activeIncidents)
    {
        var unresolved = manifest.Where(parcel => parcel.Status is ParcelStatus.LOADED
                or ParcelStatus.IN_TRANSIT
                || (parcel.Status == ParcelStatus.PENDING_OPERATOR_ACTION
                    && parcel.PendingActionType == PendingActionType.CUSTODY_EXCEPTION))
            .ToArray();
        if (unresolved.Length == 0)
            return new ParcelTripCompletionClearanceDecision("CLEAR", [], []);

        var acknowledged = activeIncidents
            .Where(incident => incident.Type == ParcelIncidentType.UNSCANNED_HANDOFF
                && incident.Status is ParcelIncidentStatus.SEARCHING
                    or ParcelIncidentStatus.ESCALATED
                    or ParcelIncidentStatus.SEARCH_EXPIRED
                && incident.ExpectedLocation?.StartsWith(
                    "DESTINATION_STATION:",
                    StringComparison.OrdinalIgnoreCase) == true)
            .GroupBy(incident => incident.ParcelId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(incident => incident.CreatedAt).First());
        var allAcknowledged = unresolved.All(parcel => acknowledged.ContainsKey(parcel.Id));

        return new ParcelTripCompletionClearanceDecision(
            allAcknowledged ? "ACKNOWLEDGED_INCIDENTS" : "BLOCKED_RECONCILIATION",
            unresolved.Select(parcel => parcel.Id).ToArray(),
            allAcknowledged
                ? unresolved.Select(parcel => acknowledged[parcel.Id].Id).ToArray()
                : []);
    }
}
