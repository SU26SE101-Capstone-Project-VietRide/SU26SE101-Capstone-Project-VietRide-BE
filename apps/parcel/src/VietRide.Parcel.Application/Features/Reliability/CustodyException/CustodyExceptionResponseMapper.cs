using System.Text.Json;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Reliability.CustodyException;

internal static class CustodyExceptionResponseMapper
{
    public static ReportCustodyExceptionResponse Map(
        ParcelCustodyExceptionRequest request,
        ParcelIncident incident,
        IReadOnlyList<string> availableActions)
        => new(
            request.Id,
            request.ParcelId,
            request.IncidentId,
            request.IncidentType.ToString(),
            incident.Status.ToString(),
            request.Status.ToString(),
            request.ActualLocationType.ToString(),
            request.ActualLocationId,
            request.LocationSnapshot,
            request.TemporaryExceptionTag,
            request.Description,
            request.ObservedWeightKg,
            DeserializeEvidence(request.EvidenceReferencesJson),
            request.Reason,
            request.ReportedByUserId,
            request.ReportedByRole,
            request.ReportedAt,
            request.ReviewedByUserId,
            request.ReviewedAt,
            request.ReviewedByRole,
            request.ReviewNote,
            request.ApprovedCustodyEventId,
            request.Status == ParcelCustodyExceptionRequestStatus.PENDING_APPROVAL
                ? null
                : incident.SearchDeadline,
            availableActions);

    private static IReadOnlyList<string> DeserializeEvidence(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
