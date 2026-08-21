using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Services;

internal static class ParcelReliabilityActionResolver
{
    public static IReadOnlyList<string> Passenger(
        VietRide.Parcel.Domain.Entities.Parcel parcel,
        ParcelIncident? incident,
        ParcelClaim? claim,
        bool isSender)
    {
        var actions = new List<string>();
        if (incident is null && parcel.Status is not (
            ParcelStatus.CANCELLED or ParcelStatus.REJECTED or ParcelStatus.EXPIRED or ParcelStatus.RETURNED))
            actions.Add("REPORT_INCIDENT");
        if (!isSender)
            return actions;
        if (incident?.Status == ParcelIncidentStatus.LOST_CONFIRMED && claim is null)
            actions.Add("SUBMIT_CLAIM");
        if (claim?.Status is ParcelClaimStatus.SUBMITTED or ParcelClaimStatus.UNDER_REVIEW)
            actions.Add("ADD_EVIDENCE");
        if (claim?.Status is ParcelClaimStatus.PAID or ParcelClaimStatus.REJECTED)
            actions.Add("APPEAL");
        return actions;
    }

    public static IReadOnlyList<string> Assistant(
        VietRide.Parcel.Domain.Entities.Parcel parcel,
        bool hasIncident)
    {
        var actions = new List<string> { "CUSTODY_SCAN" };
        switch (parcel.Status)
        {
            case ParcelStatus.RESERVED:
            case ParcelStatus.PENDING:
                actions.Add("CHECK_IN");
                break;
            case ParcelStatus.READY_TO_LOAD:
                actions.Add("LOAD");
                break;
            case ParcelStatus.IN_TRANSIT:
                actions.Add("UNLOAD");
                actions.Add("CUSTODY_EXCEPTION");
                break;
            case ParcelStatus.LOADED:
                actions.Add("CUSTODY_EXCEPTION");
                break;
            case ParcelStatus.UNLOADED:
                actions.Add("DELIVER");
                break;
        }

        if (hasIncident)
            actions.Add("VIEW_INCIDENT");
        return actions.Distinct().ToArray();
    }

    public static IReadOnlyList<string> Operator(
        ParcelIncident incident,
        ParcelClaim? claim,
        DateTimeOffset now)
    {
        var actions = incident.Status switch
        {
            ParcelIncidentStatus.OPEN or ParcelIncidentStatus.SEARCHING or ParcelIncidentStatus.ESCALATED
                => new List<string> { "ASSIGN", "RECORD_SEARCH", "MARK_FOUND" },
            ParcelIncidentStatus.SEARCH_EXPIRED
                => new List<string> { "ASSIGN", "RECORD_SEARCH", "MARK_FOUND", "DECLARE_LOST" },
            ParcelIncidentStatus.FOUND
                => new List<string> { "FORWARD", "RESOLVE" },
            ParcelIncidentStatus.FORWARDING
                => new List<string> { "RESOLVE" },
            _ => [],
        };
        if (incident.SearchDeadline <= now
            && incident.Status == ParcelIncidentStatus.ESCALATED)
            actions.Add("DECLARE_LOST");
        if (claim?.Status is ParcelClaimStatus.SUBMITTED or ParcelClaimStatus.UNDER_REVIEW)
            actions.Add("DECIDE_CLAIM");
        return actions.Distinct().ToArray();
    }
}
