using System.Text.Json;
using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Application.Features.Reliability.UnidentifiedPackages;

internal static class UnidentifiedPackageReadModelMapper
{
    public static UnidentifiedPackageResponse Map(
        UnidentifiedParcelPackage package,
        ReliabilityTripResponse? trip,
        VietRide.Parcel.Domain.Entities.Parcel? matchedParcel)
        => new(
            package.Id,
            package.TemporaryExceptionTag,
            package.OperatorId,
            package.Status.ToString(),
            package.LocationType.ToString(),
            package.LocationId,
            package.MatchedParcelId,
            package.CreatedAt,
            package.TripId,
            package.LocationSnapshot,
            package.Description,
            package.ObservedWeightKg,
            Deserialize(package.EvidenceReferencesJson),
            package.CreatedByUserId,
            package.MatchedAt,
            package.MatchedByUserId,
            trip,
            matchedParcel is null ? null : ListParcelIncidentsQueryHandler.MapParcel(matchedParcel),
            package.Status == Domain.Enums.UnidentifiedParcelPackageStatus.UNIDENTIFIED
                ? ["VIEW_MATCH_CANDIDATES", "MATCH"]
                : []);

    private static IReadOnlyList<string> Deserialize(string json)
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
