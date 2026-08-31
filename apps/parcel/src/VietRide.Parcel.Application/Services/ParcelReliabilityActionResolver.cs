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
        if (incident is null && ParcelIncidentReportPolicy.CanPassengerReport(parcel.Status))
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
        bool hasIncident,
        string? incidentType = null,
        string? incidentStatus = null,
        bool allowDirectCustodyScan = false)
    {
        var actions = new List<string>();
        switch (parcel.Status)
        {
            case ParcelStatus.RESERVED:
                actions.Add("CHECK_IN");
                break;
            case ParcelStatus.CHECKED_IN:
                actions.Add("REWEIGH");
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
            case ParcelStatus.DELIVERED_PENDING_CONFIRM:
                actions.Add("MANUAL_CONFIRM");
                actions.Add("RESEND_DELIVERY_EMAIL");
                break;
        }

        if (hasIncident)
            actions.Add("VIEW_INCIDENT");
        if (allowDirectCustodyScan
            && parcel.Status is ParcelStatus.LOADED or ParcelStatus.IN_TRANSIT or ParcelStatus.UNLOADED)
            actions.Add("CUSTODY_SCAN");
        if (parcel.Status == ParcelStatus.PENDING_OPERATOR_ACTION
            && parcel.PendingActionType == PendingActionType.CUSTODY_EXCEPTION
            && parcel.PendingActionResumeStatus is ParcelStatus.LOADED or ParcelStatus.IN_TRANSIT
            && (string.Equals(incidentType, ParcelIncidentType.MISSING.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    incidentType,
                    ParcelIncidentType.MISSING_AFTER_DEPARTURE.ToString(),
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    incidentType,
                    ParcelIncidentType.UNSCANNED_HANDOFF.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            && (string.Equals(incidentStatus, ParcelIncidentStatus.OPEN.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    incidentStatus,
                    ParcelIncidentStatus.SEARCHING.ToString(),
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    incidentStatus,
                    ParcelIncidentStatus.ESCALATED.ToString(),
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    incidentStatus,
                    ParcelIncidentStatus.SEARCH_EXPIRED.ToString(),
                    StringComparison.OrdinalIgnoreCase)))
            actions.Add("CONFIRM_FOUND_ON_VEHICLE");
        return actions.Distinct().ToArray();
    }

    public static IReadOnlyList<string> Driver(
        bool hasIncident,
        bool hasPendingCustodyExceptionApproval)
    {
        var actions = new List<string>();
        if (hasIncident)
            actions.Add("VIEW_INCIDENT");
        if (hasPendingCustodyExceptionApproval)
        {
            actions.Add("APPROVE_CUSTODY_EXCEPTION");
            actions.Add("REJECT_CUSTODY_EXCEPTION");
        }
        return actions;
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
        if (incident.SearchDeadline.HasValue
            && incident.SearchDeadline.Value <= now
            && incident.Status == ParcelIncidentStatus.ESCALATED)
            actions.Add("DECLARE_LOST");
        if (claim?.Status is ParcelClaimStatus.SUBMITTED or ParcelClaimStatus.UNDER_REVIEW)
            actions.Add("DECIDE_CLAIM");
        return actions.Distinct().ToArray();
    }
}
