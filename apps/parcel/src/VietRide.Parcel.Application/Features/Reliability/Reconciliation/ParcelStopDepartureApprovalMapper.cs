using System.Text.Json;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

internal static class ParcelStopDepartureApprovalMapper
{
    public static ParcelStopDepartureApprovalResponse Map(
        ParcelStopDepartureApprovalRequest request)
        => new(
            request.Id,
            request.TripId,
            request.StopId,
            request.OperatorId,
            DeserializeParcelIds(request.UnresolvedParcelIdsJson),
            request.DepartureOverrideReason,
            request.Status.ToString(),
            request.RequestedByUserId,
            request.RequestedByRole,
            request.RequestedAt,
            request.ReviewedByUserId,
            request.ReviewedByRole,
            request.ReviewedAt,
            request.ReviewNote,
            request.Status == ParcelStopDepartureApprovalStatus.PENDING_APPROVAL
                ? ["APPROVE", "REJECT"]
                : []);

    public static string SerializeParcelIds(IEnumerable<Guid> parcelIds)
        => JsonSerializer.Serialize(parcelIds.OrderBy(id => id).ToArray());

    public static IReadOnlyList<Guid> DeserializeParcelIds(string json)
        => JsonSerializer.Deserialize<Guid[]>(json) ?? [];

    public static bool Matches(
        ParcelStopDepartureApprovalRequest request,
        IReadOnlyCollection<Guid> unresolvedParcelIds)
        => DeserializeParcelIds(request.UnresolvedParcelIdsJson).OrderBy(id => id)
            .SequenceEqual(unresolvedParcelIds.OrderBy(id => id));
}
